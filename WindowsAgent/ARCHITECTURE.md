# Windows Agent — Architecture

A single Blazor Server app (no n8n) that turns speech or typed text into
actions on the local Windows machine, using Claude's native tool-use loop
as the orchestrator.

## Data flow

```
mic audio ──► Azure Speech (STT) ──► text
                                       │
                                       ▼
                        ClaudeAgentOrchestrator.HandleUserMessageAsync
                                       │
                       ClaudeClientFactory.Current (Settings-page choice)
                          │                              │
                   AnthropicClient                BedrockClaudeClient
                   POST /v1/messages              Bedrock Converse API
                   (Anthropic API)                 (AWS credentials)
                          │                              │
                          └──────────────┬───────────────┘
                                         ▼
                     stop_reason == "tool_use"?
                        │no                  │yes
                        ▼                    ▼
                  return text         for each tool_use block:
                        │               - IAgentTool.DescribeCall()
                        │               - RequiresConfirmation?
                        │                    → BlazorConfirmationService
                        │                      (modal in Chat.razor, awaited)
                        │               - ExecuteAsync() → ToolResult
                        │               - append tool_result, loop again
                        ▼
                Azure Speech (TTS) ──► <audio> element in the browser
```

Both Claude backends translate to/from the same internal
`AnthropicMessage`/`ContentBlock`/`AnthropicResponse` shapes, so
`ClaudeAgentOrchestrator` and every `IAgentTool` are provider-agnostic —
switching providers on the Settings page changes nothing else in the loop.

ElevenLabs (`Voice/ElevenLabsService.cs`) is still in the tree but not wired
into `Program.cs`/`Chat.razor` — parked in favor of Azure Speech's own TTS,
which needed no second vendor account.

Everything runs in-process. There is no separate service boundary between
"orchestrator" and "hands" the way the n8n version had one — the tradeoff is
you lose the visual workflow editor, but you gain a single debuggable call
stack and no HTTP hop between the model deciding on an action and the
action running, which matters once PowerShell/UI-automation tools are in
the loop and latency compounds.

## Project layout

```
WindowsAgent/
  Agent/
    IAgentTool.cs           tool contract every action implements
    ToolResult.cs
    ToolRegistry.cs          DI-populated list of tools → per-provider tool defs
    Tools/
      OpenAppTool.cs         Process.Start, allowlisted
      RunPowerShellTool.cs   PowerShell SDK, timeout-bound, confirm required
      SendEmailTool.cs       Microsoft Graph (not Outlook UI automation)
      ReadCalendarTool.cs    Graph: upcoming M365/Outlook calendar events
      ReadEmailTool.cs       Graph: unread/flagged M365/Outlook messages
      GraphClientFactory.cs  shared GraphServiceClient builder for the three tools above
      ReadGmailTool.cs       Gmail API, via GoogleAuthService
      ReadGoogleCalendarTool.cs   Google Calendar API, via GoogleAuthService
      ClaudeCodeTool.cs      shells out to `claude -p ... --output-format json`
  Orchestrator/
    IClaudeClient.cs             one CreateMessageAsync contract both backends implement
    AnthropicClient.cs           raw HTTP wrapper for /v1/messages (no official C# SDK exists)
    BedrockClaudeClient.cs       AWS Bedrock Converse API, translated to the same DTOs
    ClaudeClientFactory.cs       picks AnthropicClient vs BedrockClaudeClient per AppSettingsStore.Current.Provider
    IConfirmationService.cs
    ClaudeAgentOrchestrator.cs   the tool-use loop
  Voice/
    AzureSpeechService.cs    STT and TTS (native, no second voice vendor)
    ElevenLabsService.cs     TTS — present but unwired, see "Known gaps"
  Google/
    GoogleAuthService.cs     one-time interactive OAuth consent + silent token refresh for Gmail/Calendar
  Configuration/
    Options.cs               appsettings.json-bound seed defaults
    AppSettingsStore.cs       live settings (provider, credentials, model) edited from /settings, persisted to %LOCALAPPDATA%\WindowsAgent\settings.json
  Components/
    Layout/MainLayout.razor  Chat / Settings nav
    BlazorConfirmationService.cs   implements IConfirmationService via TaskCompletionSource
    Pages/Chat.razor         mic button, transcript, confirm modal
    Pages/Settings.razor     provider + credentials + Azure Speech config, backed by AppSettingsStore
  wwwroot/js/agent.js        Web Audio API mic capture (real PCM WAV) + TTS playback
  Program.cs                 DI wiring
  appsettings.json
```

## Adding a new tool

1. Implement `IAgentTool` in `Agent/Tools/`.
2. Register it in `Program.cs`: `builder.Services.AddSingleton<IAgentTool, YourTool>();`
3. Set `RequiresConfirmation = true` for anything that sends, deletes, modifies
   files outside a scratch folder, or spends money/API credits.

Nothing else needs to change — `ToolRegistry` picks it up automatically and
Claude sees it the next time it lists tools.

## Confirmation flow (per your "no service account yet, but confirm everything" requirement)

`RunPowerShellTool`, `SendEmailTool`, and `ClaudeCodeTool` all set
`RequiresConfirmation = true`. `OpenAppTool` doesn't, since it's restricted
to an allowlist and launching an app is low-risk — flip it to `true` if
you'd rather confirm literally everything at first and loosen later.

The gate itself: `ClaudeAgentOrchestrator` calls
`IConfirmationService.RequestConfirmationAsync(...)` and `await`s it before
running the tool. `BlazorConfirmationService` resolves that await when the
user clicks Approve/Deny in the modal — the C# call stack is genuinely
suspended mid-tool-loop, not polling, so there's no race between the model
moving on and the user answering.

## Configuring providers — Settings page vs. appsettings.json

`appsettings.json` (`Anthropic`, `Bedrock`, `AzureSpeech` sections) is only
consulted once, to seed `AppSettingsStore` on a machine with no settings
file yet. After that, everything credentials/model-related is edited from
`/settings` in the running app and persisted to
`%LOCALAPPDATA%\WindowsAgent\settings.json` — editing `appsettings.json`
after first run has no effect. This means secrets don't need to live in the
repo-adjacent publish output; they live in a per-machine file outside it
(and outside git — see `.gitignore`). Both files are plaintext, matching
this app's existing risk posture (see "Auth" below) — there's no secrets
vault here, just parity with how the API key was already being handled.

`ClaudeClientFactory` reads `AppSettingsStore.Current.Provider` on every
turn, so flipping Anthropic ⇄ Bedrock on the Settings page takes effect on
the next message with no restart.

## Reading calendar/mail: M365 via Graph, Gmail via Google OAuth

Two genuinely separate integrations, because M365 and Gmail have no shared
API — even though Outlook's desktop client can show both accounts in one
inbox, Microsoft Graph only ever sees the M365 mailbox:

- **M365/Outlook** (`ReadCalendarTool`, `ReadEmailTool`): reuse
  `GraphClientFactory`'s existing app-only `GraphServiceClient` — the same
  Entra app registration `SendEmailTool` already uses, just with
  `Calendars.Read`/`Mail.Read` added. No new settings, no new auth flow.
- **Gmail/Google Calendar** (`ReadGmailTool`, `ReadGoogleCalendarTool`):
  Google has no app-only/service-account path into a *personal* Gmail
  account the way Graph does for M365 (service accounts only work with
  Google Workspace domain-wide delegation). This needs a real, if one-time,
  interactive OAuth consent — `GoogleAuthService.ConnectAsync()`, wired to
  the "Connect Google Account" button on `/settings`, uses
  `GoogleWebAuthorizationBroker`'s standard "installed app" flow: it opens
  your default browser to Google's consent screen and spins up a temporary
  local HTTP listener to catch the redirect. **This only works because the
  app runs on the same desktop you're sitting at** — it would break if this
  were ever hosted remotely. The resulting refresh token is persisted via
  `FileDataStore` under `%LOCALAPPDATA%\WindowsAgent\google-tokens`, so
  after that one-time consent, every subsequent call refreshes silently.

  `GoogleSettings.Connected` (in `AppSettingsStore`) gates
  `GoogleAuthService.GetCredentialAsync` — a tool call fails fast with a
  clear message if you've never connected, rather than risking an
  unexpected browser popup mid-conversation if a stored token ever went
  missing. Both Google scopes are read-only
  (`gmail.readonly`/`calendar.readonly`) since this integration is for
  finding things to follow up on, not sending or modifying anything.

## Setup checklist

- **Anthropic**: API key + model, set from `/settings` (Claude provider ==
  Anthropic API). Model is `claude-sonnet-5` by default; drop to
  `claude-haiku-4-5-20251001` if you want faster/cheaper responses for
  simple routing and reserve Sonnet for anything going through
  `generate_code`.
- **AWS Bedrock** (alternative to the direct Anthropic API): from
  `/settings`, switch the provider to AWS Bedrock and either supply an
  Access Key ID / Secret Access Key for an IAM principal with
  `bedrock:InvokeModel`/`bedrock:Converse` on the target model, or check
  "use default AWS credential chain" to pick up credentials from the
  environment/shared config/IAM role instead. You'll also need the target
  model's Bedrock model ID (or inference-profile ARN) for your region, and
  **model access granted** for it in the Bedrock console — that's a
  separate, one-time per-account step from IAM permissions.
- **Azure Speech**: free F0 tier covers light personal use (check current
  quota in the Azure portal — it changes). Subscription key + region + TTS
  voice, set from `/settings`; used for both the mic's speech-to-text and
  the spoken reply (text-to-speech).
- **Microsoft Graph** (for `send_email`, `read_calendar`, `read_email`):
  register an app in Entra ID, grant **Mail.Send**, **Mail.Read**, and
  **Calendars.Read** as *application* (not delegated) permissions, get
  admin consent, and — important — scope it with an
  [application access policy](https://learn.microsoft.com/en-us/graph/auth-limit-mailbox-access)
  so the app can only access one mailbox, not every mailbox in the tenant.
  Fill in `Graph:TenantId/ClientId/ClientSecret/SenderUserPrincipalName`.
- **Google** (for `read_gmail`, `read_google_calendar`): in Google Cloud
  Console, create a project, enable the Gmail API and Google Calendar API,
  and create an OAuth client of type **Desktop app** (not Web application —
  a Desktop app client doesn't need a fixed redirect URI registered, since
  `GoogleWebAuthorizationBroker` picks a free local port at consent time).
  Enter its Client ID/Secret on `/settings` and click **Connect Google
  Account**; a browser window opens for one-time consent, and if the OAuth
  consent screen is still in "Testing" publishing status, add your Google
  account as a test user first or the consent screen will reject it.
- **Claude Code**: make sure the `claude` CLI is on PATH (or set the full
  path in `Agent:ClaudeCodeExecutablePath`), and point
  `Agent:ClaudeCodeWorkingDirectory` at a scratch folder you're fine with it
  writing files into.
- **PowerShell SDK**: `Microsoft.PowerShell.SDK` pulls in the engine
  in-process — no separate `powershell.exe` dependency, but it's a sizeable
  package; first restore will take a minute.

## Voice pipeline: WAV capture and the SignalR message-size limit

Two bugs compounded to make voice input crash the Blazor circuit instead of
just failing cleanly, both now fixed:

1. **Audio format.** `agent.js` used to record via `MediaRecorder` asking for
   `audio/wav`, but browsers ignore that and record WebM/Opus regardless —
   Azure Speech got bytes labeled WAV that weren't. Fixed by recording real
   PCM via the Web Audio API (`AudioContext`/`ScriptProcessorNode`),
   downsampling to 16kHz, and writing a genuine canonical WAV header
   client-side. `AzureSpeechService.TranscribeAsync` now takes that `byte[]`
   and parses the actual RIFF/`fmt `/`data` chunks rather than assuming a
   fixed 44-byte layout.
2. **SignalR message size.** Even with real WAV bytes, the JS→.NET interop
   call carrying them (`micRecorder.stop()`'s return value, base64-encoded)
   silently killed the circuit for anything but the shortest utterance.
   `HubOptions.MaximumReceiveMessageSize` defaults to **32KB**, and a few
   seconds of 16kHz/16-bit mono audio blows past that — SignalR just closes
   the connection ("Server returned an error on close"), which surfaces to
   the browser as "Attempting to reconnect" with no application-level
   exception anywhere to catch. `Program.cs` raises this to 5MB via
   `.AddHubOptions(...)`. If you need longer recordings than that covers
   (~a couple of minutes), the documented alternative is streaming JS
   interop instead of one big return value — see the "Maximum receive
   message size" section of Microsoft's Blazor SignalR guidance.

`AzureSpeechService` also now runs the whole Speech SDK call sequence via
`Task.Run` bounded by a 15s `WaitAsync` (not just the final async call, since
the SDK does some blocking synchronous setup too) — real defensive value for
a slow/unreachable Azure endpoint, independent of the two bugs above. And
`Chat.razor` catches exceptions around both the voice and turn-handling paths
so any future failure here becomes a visible `[error]` log line instead of
an unhandled exception tearing down the circuit again.

## Known gaps / next steps

- **No UI-automation tool yet.** For anything without a clean API (most
  legacy Win32 apps), add a `FlaUI`-based tool later — same `IAgentTool`
  shape, just with `RequiresConfirmation = true` and a narrower, per-app
  set of actions rather than a generic "click at X,Y."
- **Multi-turn context growth**: `ClaudeAgentOrchestrator._history` grows
  unbounded for the life of a circuit. Fine for a session; add a trim/reset
  once you're running it for hours at a stretch.
- **Auth**: there's no login on the Blazor app itself. Since it's driving
  real actions on the machine — and `/settings` now holds plaintext API
  keys/AWS secrets — at minimum bind Kestrel to localhost only until you're
  ready to add real auth.
- **ElevenLabs parked, not removed.** `Voice/ElevenLabsService.cs` and its
  `AddHttpClient<ElevenLabsService>()` registration in `Program.cs` are
  still in the tree, but nothing injects it anymore — Azure Speech's own
  TTS (`AzureSpeechService.SynthesizeAsync`) covers that need natively for
  now. Revive it later by re-adding `@inject ElevenLabsService Tts` in
  `Chat.razor` and swapping the `Speech.SynthesizeAsync` call for
  `Tts.SynthesizeAsync`.

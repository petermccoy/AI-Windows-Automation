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
                     POST /v1/messages (Anthropic API, tools attached)
                                       │
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
                 ElevenLabs (TTS) ──► <audio> element in the browser
```

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
    ToolRegistry.cs          DI-populated list of tools → Anthropic tool defs
    Tools/
      OpenAppTool.cs         Process.Start, allowlisted
      RunPowerShellTool.cs   PowerShell SDK, timeout-bound, confirm required
      SendEmailTool.cs       Microsoft Graph (not Outlook UI automation)
      ClaudeCodeTool.cs      shells out to `claude -p ... --output-format json`
  Orchestrator/
    AnthropicClient.cs       raw HTTP wrapper for /v1/messages (no official C# SDK exists)
    IConfirmationService.cs
    ClaudeAgentOrchestrator.cs   the tool-use loop
  Voice/
    AzureSpeechService.cs    STT
    ElevenLabsService.cs     TTS
  Components/
    BlazorConfirmationService.cs   implements IConfirmationService via TaskCompletionSource
    Pages/Chat.razor         mic button, transcript, confirm modal
  wwwroot/js/agent.js        MediaRecorder mic capture + TTS playback
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

## Setup checklist

- **Anthropic**: API key in `appsettings.json` → `Anthropic:ApiKey`. Model is
  set to `claude-sonnet-5`; drop to `claude-haiku-4-5-20251001` if you want
  faster/cheaper responses for simple routing and reserve Sonnet for
  anything going through `generate_code`.
- **Azure Speech**: free F0 tier covers light personal use (check current
  quota in the Azure portal — it changes). Key + region under `AzureSpeech`.
- **ElevenLabs**: resurrect the account, grab an API key and a voice ID,
  drop them under `ElevenLabs`.
- **Microsoft Graph** (for `send_email`): register an app in Entra ID,
  grant **Mail.Send** as an *application* (not delegated) permission, get
  admin consent, and — important — scope it with an
  [application access policy](https://learn.microsoft.com/en-us/graph/auth-limit-mailbox-access)
  so the app can only send as one mailbox, not every mailbox in the tenant.
  Fill in `Graph:TenantId/ClientId/ClientSecret/SenderUserPrincipalName`.
- **Claude Code**: make sure the `claude` CLI is on PATH (or set the full
  path in `Agent:ClaudeCodeExecutablePath`), and point
  `Agent:ClaudeCodeWorkingDirectory` at a scratch folder you're fine with it
  writing files into.
- **PowerShell SDK**: `Microsoft.PowerShell.SDK` pulls in the engine
  in-process — no separate `powershell.exe` dependency, but it's a sizeable
  package; first restore will take a minute.

## Known gaps / next steps

- **Audio format**: `agent.js` records `audio/wav` via `MediaRecorder`,
  which most browsers actually emit as WebM/Opus regardless of the
  requested mime type. Azure Speech wants real PCM WAV — you'll likely need
  to either transcode client-side (Web Audio API) or decode server-side
  (e.g. with `NAudio`) before calling `AzureSpeechService.TranscribeAsync`.
  Flagged here rather than silently papered over.
- **No UI-automation tool yet.** For anything without a clean API (most
  legacy Win32 apps), add a `FlaUI`-based tool later — same `IAgentTool`
  shape, just with `RequiresConfirmation = true` and a narrower, per-app
  set of actions rather than a generic "click at X,Y."
- **Multi-turn context growth**: `ClaudeAgentOrchestrator._history` grows
  unbounded for the life of a circuit. Fine for a session; add a trim/reset
  once you're running it for hours at a stretch.
- **Auth**: there's no login on the Blazor app itself. Since it's driving
  real actions on the machine, at minimum bind Kestrel to localhost only
  until you're ready to add real auth.

using WindowsAgent.Agent;
using WindowsAgent.Agent.Tools;
using WindowsAgent.Components;
using WindowsAgent.Configuration;
using WindowsAgent.Google;
using WindowsAgent.Orchestrator;
using WindowsAgent.Voice;

var builder = WebApplication.CreateBuilder(args);

// --- Options binding ---
// Anthropic/Bedrock/AzureSpeech here are only the seed defaults for a brand-new
// install — AppSettingsStore owns the live, Settings-page-editable copy after that.
builder.Services.Configure<AnthropicOptions>(builder.Configuration.GetSection("Anthropic"));
builder.Services.Configure<BedrockOptions>(builder.Configuration.GetSection("Bedrock"));
builder.Services.Configure<AzureSpeechOptions>(builder.Configuration.GetSection("AzureSpeech"));
builder.Services.Configure<ElevenLabsOptions>(builder.Configuration.GetSection("ElevenLabs"));
builder.Services.Configure<GraphOptions>(builder.Configuration.GetSection("Graph"));
builder.Services.Configure<GoogleOptions>(builder.Configuration.GetSection("Google"));
builder.Services.Configure<AgentOptions>(builder.Configuration.GetSection("Agent"));
builder.Services.AddSingleton<AppSettingsStore>();
builder.Services.AddSingleton<GraphClientFactory>();
builder.Services.AddSingleton<GoogleAuthService>();

// --- Blazor Server ---
// SignalR's default MaximumReceiveMessageSize is 32KB. The mic recorder sends its
// whole WAV recording back from the browser as one base64 JS-interop return value
// (agent.js's micRecorder.stop()), which blows past that for anything but the
// shortest utterance — the server silently kills the connection rather than
// erroring cleanly ("Server returned an error on close"). 5MB covers a couple of
// minutes of 16kHz/16-bit mono audio; see ARCHITECTURE.md for the tradeoff.
builder.Services.AddRazorComponents().AddInteractiveServerComponents()
    .AddHubOptions(options => options.MaximumReceiveMessageSize = 5 * 1024 * 1024);

// --- Tools (add new tools here — each is auto-registered into ToolRegistry) ---
builder.Services.AddSingleton<IAgentTool, OpenAppTool>();
builder.Services.AddSingleton<IAgentTool, RunPowerShellTool>();
builder.Services.AddSingleton<IAgentTool, SendEmailTool>();
builder.Services.AddSingleton<IAgentTool, ReadCalendarTool>();
builder.Services.AddSingleton<IAgentTool, ReadEmailTool>();
builder.Services.AddSingleton<IAgentTool, ReadGmailTool>();
builder.Services.AddSingleton<IAgentTool, ReadGoogleCalendarTool>();
builder.Services.AddSingleton<IAgentTool, ClaudeCodeTool>();
builder.Services.AddSingleton<ToolRegistry>();

// --- Orchestrator + confirmation gate ---
// Scoped: one orchestrator + one pending confirmation per browser circuit,
// so two users (or two tabs) never cross-confirm each other's actions.
builder.Services.AddHttpClient<AnthropicClient>();
builder.Services.AddScoped<BedrockClaudeClient>();
builder.Services.AddScoped<ClaudeClientFactory>();
builder.Services.AddScoped<BlazorConfirmationService>();
builder.Services.AddScoped<IConfirmationService>(sp => sp.GetRequiredService<BlazorConfirmationService>());
builder.Services.AddScoped<ClaudeAgentOrchestrator>();

// --- Voice ---
builder.Services.AddSingleton<AzureSpeechService>();
builder.Services.AddHttpClient<ElevenLabsService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();

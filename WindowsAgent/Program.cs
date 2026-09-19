using WindowsAgent.Agent;
using WindowsAgent.Agent.Tools;
using WindowsAgent.Components;
using WindowsAgent.Configuration;
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
builder.Services.Configure<AgentOptions>(builder.Configuration.GetSection("Agent"));
builder.Services.AddSingleton<AppSettingsStore>();

// --- Blazor Server ---
builder.Services.AddRazorComponents().AddInteractiveServerComponents();

// --- Tools (add new tools here — each is auto-registered into ToolRegistry) ---
builder.Services.AddSingleton<IAgentTool, OpenAppTool>();
builder.Services.AddSingleton<IAgentTool, RunPowerShellTool>();
builder.Services.AddSingleton<IAgentTool, SendEmailTool>();
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

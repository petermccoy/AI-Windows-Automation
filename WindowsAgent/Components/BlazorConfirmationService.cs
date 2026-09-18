using WindowsAgent.Orchestrator;

namespace WindowsAgent.Components;

/// <summary>
/// Scoped per-circuit (per browser tab). The orchestrator calls
/// RequestConfirmationAsync and awaits it; the Chat page subscribes to
/// PendingRequestChanged to show a modal, then calls Resolve() on click.
/// </summary>
public class BlazorConfirmationService : IConfirmationService
{
    private TaskCompletionSource<bool>? _pending;

    public (string ToolName, string Description)? PendingRequest { get; private set; }
    public event Action? PendingRequestChanged;

    public Task<bool> RequestConfirmationAsync(string toolName, string description, CancellationToken ct)
    {
        _pending = new TaskCompletionSource<bool>();
        PendingRequest = (toolName, description);
        PendingRequestChanged?.Invoke();

        ct.Register(() => _pending?.TrySetResult(false));

        return _pending.Task;
    }

    public void Resolve(bool approved)
    {
        _pending?.TrySetResult(approved);
        PendingRequest = null;
        PendingRequestChanged?.Invoke();
    }
}

namespace WindowsAgent.Orchestrator;

/// <summary>
/// Pauses the tool-call loop and waits for an explicit yes/no from the user
/// before a confirmation-required tool executes. Implemented by the Blazor
/// component so it can show a modal and resolve the task on button click.
/// </summary>
public interface IConfirmationService
{
    Task<bool> RequestConfirmationAsync(string toolName, string description, CancellationToken ct);
}

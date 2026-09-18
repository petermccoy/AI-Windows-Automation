namespace WindowsAgent.Agent;

public class ToolRegistry
{
    private readonly Dictionary<string, IAgentTool> _tools;

    public ToolRegistry(IEnumerable<IAgentTool> tools)
    {
        _tools = tools.ToDictionary(t => t.Name, t => t);
    }

    public IReadOnlyCollection<IAgentTool> All => _tools.Values;

    public bool TryGet(string name, out IAgentTool tool) => _tools.TryGetValue(name, out tool!);

    /// <summary>Shapes tool metadata into Anthropic's "tools" request format.</summary>
    public object[] ToAnthropicToolDefinitions() =>
        _tools.Values.Select(t => new
        {
            name = t.Name,
            description = t.Description,
            input_schema = t.InputSchema
        }).ToArray();
}

namespace SB.FlowEngine.Execution;

public sealed class FlowNodeContext {
    public string ModuleId { get; init; } = "";
    public IReadOnlyDictionary<string, object?> Config { get; init; }
        = new Dictionary<string, object?>();
    public IReadOnlyDictionary<string, object?> Inputs { get; init; }
        = new Dictionary<string, object?>();
}

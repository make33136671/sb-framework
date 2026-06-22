namespace SB.FlowEngine.Execution;

public sealed class FlowExecutionResult {
    public IReadOnlyDictionary<string, object?> Variables { get; init; }
        = new Dictionary<string, object?>();
    public int Steps { get; init; }
}

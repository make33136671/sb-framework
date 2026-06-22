namespace SB.FlowEngine.Execution;

public sealed class FlowExecutionOptions {
    public int MaxSteps { get; set; } = 100_000;
    /// <summary>节点状态回调: (nodeId, "running"|"success"|"failed")</summary>
    public Action<string, string>? OnNodeStatus { get; set; }
}

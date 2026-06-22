namespace SB.FlowEngine.Execution;

/// <summary>业务节点执行器。业务层为每个节点类型实现一个,引擎按 ModuleId 分发。</summary>
public interface IFlowNodeExecutor {
    string ModuleId { get; }
    Task<IReadOnlyDictionary<string, object?>> ExecuteAsync(
        FlowNodeContext ctx, CancellationToken ct = default);
}

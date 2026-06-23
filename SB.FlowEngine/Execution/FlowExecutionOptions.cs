using SB.FlowEngine.Contracts;

namespace SB.FlowEngine.Execution;

public sealed class FlowExecutionOptions {
    public int MaxSteps { get; set; } = 100_000;

    /// <summary>节点状态回调: (nodeId, "running"|"success"|"failed")</summary>
    public Action<string, string>? OnNodeStatus { get; set; }

    /// <summary>子流程(subFlow 节点)递归调用的最大深度,防止环。</summary>
    public int MaxSubFlowDepth { get; set; } = 5;

    /// <summary>子流程解析器:subFlow 节点按其 config.flowId 取出子图。
    /// 引擎不绑数据库,由宿主(avm)注入「从哪取子图」。返回 null 表示找不到。
    /// 若节点 config 直接带 inlineGraph,则优先用 inline,不调此回调。</summary>
    public Func<string, FlowGraph?>? SubFlowResolver { get; set; }
}

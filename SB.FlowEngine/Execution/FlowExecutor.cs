using SB.FlowEngine.Contracts;

namespace SB.FlowEngine.Execution;

/// <summary>图执行引擎:执行指针沿 exec 线走 + 数据沿 data 线拉取求值(只读缓存)。
/// 与前端 @saibo/flow-engine 执行器语义对齐。</summary>
public sealed class FlowExecutor {
    private static readonly HashSet<string> ControlIds = new()
        { "if", "loop", "goto", "label", "varGet", "varSet" };

    private readonly Dictionary<string, IFlowNodeExecutor> _executors;

    public FlowExecutor(IEnumerable<IFlowNodeExecutor> executors) {
        _executors = new();
        foreach (var e in executors) {
            if (!_executors.TryAdd(e.ModuleId, e))
                throw new FlowExecutionException($"重复的节点执行器 ModuleId: {e.ModuleId}");
        }
    }

    public async Task<FlowExecutionResult> RunAsync(
        FlowGraph graph, IReadOnlyList<ModuleDefinition> modules,
        FlowExecutionOptions? options = null, CancellationToken ct = default) {
        options ??= new FlowExecutionOptions();
        var nodeMap = graph.Nodes.ToDictionary(n => n.Id);
        var moduleMap = modules.ToDictionary(m => m.Id);
        var variables = new Dictionary<string, object?>();
        foreach (var v in graph.Variables) variables[v.Name] = v.Initial;
        var nodeOutputs = new Dictionary<string, IReadOnlyDictionary<string, object?>>();
        var loopCounters = new Dictionary<string, int>();

        // 沿 data 线拉取某节点的输入
        IReadOnlyDictionary<string, object?> GatherInputs(string nodeId) {
            var inputs = new Dictionary<string, object?>();
            foreach (var e in graph.Edges) {
                if (e.Kind != "data" || e.Target != nodeId) continue;
                if (!nodeMap.TryGetValue(e.Source, out var up)) continue;
                if (up.ModuleId == "varGet") {
                    var varName = up.Config.TryGetValue("varName", out var vn) ? AsString(vn) : null;
                    inputs[e.TargetPort] = varName != null && variables.TryGetValue(varName, out var val) ? val : null;
                } else if (!ControlIds.Contains(up.ModuleId)) {
                    if (!nodeOutputs.TryGetValue(e.Source, out var outs))
                        throw new FlowExecutionException(
                            $"节点 {e.Source} 的输出在被读取前尚未执行，请检查执行顺序");
                    inputs[e.TargetPort] = outs.TryGetValue(e.SourcePort, out var ov) ? ov : null;
                }
                // 其他控制节点:忽略(无数据输出)
            }
            return inputs;
        }

        // 沿 exec 线找下一个节点
        string? NextByExec(string nodeId, string portId) {
            foreach (var e in graph.Edges)
                if (e.Kind == "exec" && e.Source == nodeId && e.SourcePort == portId)
                    return e.Target;
            return null;
        }

        string? pc = string.IsNullOrEmpty(graph.Entry) ? null : graph.Entry;
        int steps = 0;
        while (pc != null) {
            if (++steps > options.MaxSteps)
                throw new FlowExecutionException(
                    $"执行步数超过上限 {options.MaxSteps}(可能存在死循环)");
            if (!nodeMap.TryGetValue(pc, out var node)) break;
            options.OnNodeStatus?.Invoke(pc, "running");
            try {
                switch (node.ModuleId) {
                    case "if": {
                        var inputs = GatherInputs(pc);
                        var cond = inputs.TryGetValue("cond", out var c) ? c : null;
                        var branch = TruthyHelper.IsTruthy(cond) ? "true" : "false";
                        options.OnNodeStatus?.Invoke(pc, "success");
                        pc = NextByExec(pc, branch);
                        break;
                    }
                    case "loop": {
                        var inputs = GatherInputs(pc);
                        var count = ToInt(inputs.TryGetValue("count", out var ci) ? ci
                            : node.Config.TryGetValue("count", out var cc) ? cc : 0);
                        var done = loopCounters.TryGetValue(pc, out var d) ? d : 0;
                        if (done < count) {
                            loopCounters[pc] = done + 1;
                            options.OnNodeStatus?.Invoke(pc, "success");
                            pc = NextByExec(pc, "body");
                        } else {
                            loopCounters.Remove(pc);
                            options.OnNodeStatus?.Invoke(pc, "success");
                            pc = NextByExec(pc, "done");
                        }
                        break;
                    }
                    case "goto":
                        options.OnNodeStatus?.Invoke(pc, "success");
                        pc = NextByExec(pc, "to");
                        break;
                    case "label":
                        options.OnNodeStatus?.Invoke(pc, "success");
                        pc = NextByExec(pc, "out");
                        break;
                    case "varSet": {
                        var inputs = GatherInputs(pc);
                        var varName = node.Config.TryGetValue("varName", out var vn) ? AsString(vn) : null;
                        if (varName != null)
                            variables[varName] = inputs.TryGetValue("value", out var val) ? val : null;
                        options.OnNodeStatus?.Invoke(pc, "success");
                        pc = NextByExec(pc, "out");
                        break;
                    }
                    case "varGet":
                        // 纯数据节点,正常不会成为 pc(无 exec 入口);若意外到达,直接跳过
                        options.OnNodeStatus?.Invoke(pc, "success");
                        pc = null;
                        break;
                    default: {
                        // 业务节点
                        if (!_executors.TryGetValue(node.ModuleId, out var exec))
                            throw new FlowExecutionException($"未知模块: {node.ModuleId}");
                        var inputs = GatherInputs(pc);
                        var outs = await exec.ExecuteAsync(
                            new FlowNodeContext { ModuleId = node.ModuleId, Config = node.Config, Inputs = inputs },
                            ct) ?? new Dictionary<string, object?>();
                        nodeOutputs[pc] = outs;
                        options.OnNodeStatus?.Invoke(pc, "success");
                        var mod = moduleMap.TryGetValue(node.ModuleId, out var m) ? m : null;
                        var firstOut = mod?.ExecOutputs.FirstOrDefault()?.Id;
                        pc = firstOut != null ? NextByExec(pc, firstOut) : null;
                        break;
                    }
                }
            } catch {
                options.OnNodeStatus?.Invoke(pc ?? "", "failed");
                throw;
            }
        }
        return new FlowExecutionResult { Variables = variables, Steps = steps };
    }

    private static int ToInt(object? value) => value switch {
        null => 0,
        int i => i,
        long l => (int)l,
        double d => (int)d,
        float f => (int)f,
        decimal m => (int)m,
        string s => int.TryParse(s, out var r) ? r : 0,
        // FlowGraph 经 JSON 反序列化时数值是 JsonElement,需显式取数。
        System.Text.Json.JsonElement je => je.ValueKind switch {
            System.Text.Json.JsonValueKind.Number => je.TryGetInt32(out var n) ? n : (int)je.GetDouble(),
            System.Text.Json.JsonValueKind.String => int.TryParse(je.GetString(), out var sr) ? sr : 0,
            _ => 0,
        },
        _ => Convert.ToInt32(value),
    };

    /// <summary>把 object?(可能是 JsonElement)安全转成 string,用于读 varName 等配置。</summary>
    private static string? AsString(object? value) => value switch {
        null => null,
        string s => s,
        System.Text.Json.JsonElement je => je.ValueKind == System.Text.Json.JsonValueKind.String
            ? je.GetString()
            : je.ValueKind == System.Text.Json.JsonValueKind.Null ? null : je.ToString(),
        _ => value.ToString(),
    };
}

using System.Text.Json;
using SB.FlowEngine.Contracts;
using SB.FlowEngine.Execution;
using Xunit;
using FluentAssertions;

namespace SB.FlowEngine.Tests;

// 可控制成功/失败的 mock:前 N 次抛异常,其后成功;记录调用次数。
sealed class FlakyExec : IFlowNodeExecutor {
    public string ModuleId { get; }
    private readonly int _failTimes;
    public int CallCount;
    public FlakyExec(string moduleId, int failTimes) { ModuleId = moduleId; _failTimes = failTimes; }
    public Task<IReadOnlyDictionary<string, object?>> ExecuteAsync(FlowNodeContext ctx, CancellationToken ct = default) {
        CallCount++;
        if (CallCount <= _failTimes) throw new InvalidOperationException($"模拟失败第 {CallCount} 次");
        return Task.FromResult<IReadOnlyDictionary<string, object?>>(new Dictionary<string, object?>());
    }
}

public class WaitSubFlowRetryTests {
    static ModuleDefinition WaitMod() => new() { Id = "wait",
        ExecInputs = { new(){Id="in"} }, ExecOutputs = { new(){Id="out"} } };
    static ModuleDefinition SubFlowMod() => new() { Id = "subFlow",
        ExecInputs = { new(){Id="in"} }, ExecOutputs = { new(){Id="out"} } };
    static ModuleDefinition SinkMod(string id = "sink") => new() { Id = id,
        ExecInputs = { new(){Id="in"} }, ExecOutputs = { new(){Id="out"} } };

    // ---------- wait ----------
    [Fact]
    public async Task Wait_delays_then_continues() {
        bool hit = false;
        var graph = new FlowGraph { Entry = "w",
            Nodes = {
                new(){Id="w", ModuleId="wait", Config={["durationMs"]=10}},
                new(){Id="s", ModuleId="sink"},
            },
            Edges = { new(){Id="e",Source="w",SourcePort="out",Target="s",TargetPort="in",Kind="exec"} },
        };
        var mods = new List<ModuleDefinition> { WaitMod(), SinkMod() };
        var execs = new List<IFlowNodeExecutor> { new MockExec("sink", _ => { hit = true; return new Dictionary<string,object?>(); }) };
        await new FlowExecutor(execs).RunAsync(graph, mods);
        hit.Should().BeTrue();   // wait 之后正常往下走
    }

    // ---------- subFlow:inlineGraph ----------
    [Fact]
    public async Task SubFlow_runs_inline_graph() {
        var inline = new FlowGraph { Entry = "si",
            Nodes = { new(){Id="si", ModuleId="sink"} },
        };
        var graph = new FlowGraph { Entry = "sf",
            Nodes = {
                new(){Id="sf", ModuleId="subFlow", Config={["inlineGraph"]=inline}},
                new(){Id="after", ModuleId="sink"},
            },
            Edges = { new(){Id="e",Source="sf",SourcePort="out",Target="after",TargetPort="in",Kind="exec"} },
        };
        var mods = new List<ModuleDefinition> { SubFlowMod(), SinkMod() };
        int calls = 0;
        var execs = new List<IFlowNodeExecutor> { new MockExec("sink", _ => { calls++; return new Dictionary<string,object?>(); }) };
        await new FlowExecutor(execs).RunAsync(graph, mods);
        calls.Should().Be(2);   // 子图里的 sink + 主图 after 的 sink
    }

    // ---------- subFlow:resolver ----------
    [Fact]
    public async Task SubFlow_resolves_by_flowId() {
        var inline = new FlowGraph { Entry = "si", Nodes = { new(){Id="si", ModuleId="sink"} } };
        var graph = new FlowGraph { Entry = "sf",
            Nodes = { new(){Id="sf", ModuleId="subFlow", Config={["flowId"]="abc"}} },
        };
        var mods = new List<ModuleDefinition> { SubFlowMod(), SinkMod() };
        int calls = 0;
        var execs = new List<IFlowNodeExecutor> { new MockExec("sink", _ => { calls++; return new Dictionary<string,object?>(); }) };
        var opts = new FlowExecutionOptions { SubFlowResolver = id => id == "abc" ? inline : null };
        await new FlowExecutor(execs).RunAsync(graph, mods, opts);
        calls.Should().Be(1);
    }

    [Fact]
    public async Task SubFlow_depth_limit_throws() {
        // 自引用子图(resolver 总返回同一张引用自己的图)→ 应在 MaxSubFlowDepth 处抛
        FlowGraph self = null!;
        self = new FlowGraph { Entry = "sf", Nodes = { new(){Id="sf", ModuleId="subFlow", Config={["flowId"]="loop"}} } };
        var mods = new List<ModuleDefinition> { SubFlowMod() };
        var opts = new FlowExecutionOptions { MaxSubFlowDepth = 3, SubFlowResolver = _ => self };
        var act = async () => await new FlowExecutor(Array.Empty<IFlowNodeExecutor>()).RunAsync(self, mods, opts);
        await act.Should().ThrowAsync<FlowExecutionException>().WithMessage("*递归深度*");
    }

    // ---------- retry / stopOnError ----------
    [Fact]
    public async Task Retry_succeeds_after_transient_failure() {
        // 失败 2 次后成功,retry=3 → 应最终成功,共调用 3 次
        var flaky = new FlakyExec("biz", failTimes: 2);
        var graph = new FlowGraph { Entry = "b",
            Nodes = { new(){Id="b", ModuleId="biz", Config={["retry"]=3}} },
        };
        var mods = new List<ModuleDefinition> { new(){Id="biz", ExecInputs={new(){Id="in"}}, ExecOutputs={new(){Id="out"}}} };
        await new FlowExecutor(new IFlowNodeExecutor[]{flaky}).RunAsync(graph, mods);
        flaky.CallCount.Should().Be(3);
    }

    [Fact]
    public async Task Retry_exhausted_with_stopOnError_throws() {
        var flaky = new FlakyExec("biz", failTimes: 99);
        var graph = new FlowGraph { Entry = "b",
            Nodes = { new(){Id="b", ModuleId="biz", Config={["retry"]=1,["stopOnError"]=true}} },
        };
        var mods = new List<ModuleDefinition> { new(){Id="biz", ExecInputs={new(){Id="in"}}, ExecOutputs={new(){Id="out"}}} };
        var act = async () => await new FlowExecutor(new IFlowNodeExecutor[]{flaky}).RunAsync(graph, mods);
        await act.Should().ThrowAsync<InvalidOperationException>();
        flaky.CallCount.Should().Be(2);   // 初次 + 1 retry
    }

    [Fact]
    public async Task StopOnError_false_swallows_and_continues() {
        // 业务节点失败但 stopOnError=false → 不抛,继续走 out 到下游
        var flaky = new FlakyExec("biz", failTimes: 99);
        bool downstream = false;
        var graph = new FlowGraph { Entry = "b",
            Nodes = {
                new(){Id="b", ModuleId="biz", Config={["stopOnError"]=false}},
                new(){Id="d", ModuleId="sink"},
            },
            Edges = { new(){Id="e",Source="b",SourcePort="out",Target="d",TargetPort="in",Kind="exec"} },
        };
        var mods = new List<ModuleDefinition> {
            new(){Id="biz", ExecInputs={new(){Id="in"}}, ExecOutputs={new(){Id="out"}}}, SinkMod() };
        var execs = new IFlowNodeExecutor[] { flaky, new MockExec("sink", _ => { downstream = true; return new Dictionary<string,object?>(); }) };
        await new FlowExecutor(execs).RunAsync(graph, mods);
        downstream.Should().BeTrue();   // 失败被吞,继续往下
    }

    // ---------- JSON 反序列化路径(防 JsonElement 坑)----------
    [Fact]
    public async Task Wait_and_retry_config_survive_json_roundtrip() {
        // 真实前端传 camelCase JSON,config 值是 JsonElement,必须能解包
        var flaky = new FlakyExec("biz", failTimes: 1);
        var json = """
        {
          "entry": "b",
          "nodes": [
            { "id": "w", "moduleId": "wait", "config": { "durationMs": 5 } },
            { "id": "b", "moduleId": "biz", "config": { "retry": 2, "stopOnError": true } }
          ],
          "edges": [
            { "id": "e", "source": "w", "sourcePort": "out", "target": "b", "targetPort": "in", "kind": "exec" }
          ]
        }
        """;
        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var graph = JsonSerializer.Deserialize<FlowGraph>(json, opts)!;
        graph.Entry = "w";
        var mods = new List<ModuleDefinition> { WaitMod(),
            new(){Id="biz", ExecInputs={new(){Id="in"}}, ExecOutputs={new(){Id="out"}}} };
        await new FlowExecutor(new IFlowNodeExecutor[]{flaky}).RunAsync(graph, mods);
        flaky.CallCount.Should().Be(2);   // retry 从 JsonElement 正确解包 → 失败1次后成功
    }
}

using SB.FlowEngine.Contracts;
using SB.FlowEngine.Execution;
using Xunit;
using FluentAssertions;
namespace SB.FlowEngine.Tests;

public class DataPullTests {
    static ModuleDefinition EmitMod() => new(){Id="emit", ExecInputs={new(){Id="in"}}, ExecOutputs={new(){Id="out"}}, DataOutputs={new(){Id="value",Type="number"}}};
    static ModuleDefinition SinkMod() => new(){Id="sink", ExecInputs={new(){Id="in"}}, ExecOutputs={new(){Id="out"}}, DataInputs={new(){Id="v",Type="number"}}};

    [Fact]
    public async Task Business_output_flows_to_downstream() {
        object? got = null;
        var graph = new FlowGraph { Entry = "e",
            Nodes = { new(){Id="e",ModuleId="emit",Config={["n"]=7}}, new(){Id="s",ModuleId="sink"} },
            Edges = {
                new(){Id="ex",Source="e",SourcePort="out",Target="s",TargetPort="in",Kind="exec"},
                new(){Id="dx",Source="e",SourcePort="value",Target="s",TargetPort="v",Kind="data"},
            },
        };
        var mods = new List<ModuleDefinition> { EmitMod(), SinkMod() };
        var execs = new List<IFlowNodeExecutor> {
            new MockExec("emit", ctx => new Dictionary<string,object?>{["value"]=ctx.Config["n"]}),
            new MockExec("sink", ctx => { got = ctx.Inputs.TryGetValue("v", out var v) ? v : null; return new Dictionary<string,object?>(); }),
        };
        await new FlowExecutor(execs).RunAsync(graph, mods);
        got.Should().Be(7);
    }

    [Fact]
    public async Task Business_node_executes_only_once() {
        // emit 被两个下游(sink + varSet 经数据线)引用,但应只执行一次(缓存只读不重跑)
        var emit = new MockExec("emit", _ => new Dictionary<string,object?>{["value"]=5});
        var graph = new FlowGraph { Entry = "e",
            Variables = { new(){Name="x",Type="number",Initial=0} },
            Nodes = {
                new(){Id="e",ModuleId="emit"},
                new(){Id="s1",ModuleId="sink"},
                new(){Id="vs",ModuleId="varSet",Config={["varName"]="x"}},
            },
            Edges = {
                new(){Id="ex1",Source="e",SourcePort="out",Target="s1",TargetPort="in",Kind="exec"},
                new(){Id="ex2",Source="s1",SourcePort="out",Target="vs",TargetPort="in",Kind="exec"},
                new(){Id="d1",Source="e",SourcePort="value",Target="s1",TargetPort="v",Kind="data"},
                new(){Id="d2",Source="e",SourcePort="value",Target="vs",TargetPort="value",Kind="data"},
            },
        };
        var mods = new List<ModuleDefinition> { EmitMod(), SinkMod(),
            new(){Id="varSet", ExecInputs={new(){Id="in"}}, ExecOutputs={new(){Id="out"}}, DataInputs={new(){Id="value"}}} };
        var execs = new List<IFlowNodeExecutor> { emit,
            new MockExec("sink", _ => new Dictionary<string,object?>()) };
        await new FlowExecutor(execs).RunAsync(graph, mods);
        emit.CallCount.Should().Be(1); // 关键:只执行一次
    }

    [Fact]
    public async Task Reading_unexecuted_upstream_throws() {
        // sink 的数据线上游 emit 不在 exec 链上(没 exec 边到 sink 前执行) → 读时抛错
        var graph = new FlowGraph { Entry = "s",
            Nodes = { new(){Id="e",ModuleId="emit"}, new(){Id="s",ModuleId="sink"} },
            Edges = { new(){Id="dx",Source="e",SourcePort="value",Target="s",TargetPort="v",Kind="data"} },
            // 注意:entry 直接是 s,e 从未被 exec 执行
        };
        var mods = new List<ModuleDefinition> { EmitMod(), SinkMod() };
        var execs = new List<IFlowNodeExecutor> {
            new MockExec("emit", _ => new Dictionary<string,object?>{["value"]=1}),
            new MockExec("sink", _ => new Dictionary<string,object?>()) };
        var act = async () => await new FlowExecutor(execs).RunAsync(graph, mods);
        (await act.Should().ThrowAsync<FlowExecutionException>())
            .WithMessage("*尚未执行*");
    }
}

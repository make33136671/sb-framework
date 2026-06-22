using SB.FlowEngine.Contracts;
using SB.FlowEngine.Execution;
using Xunit;
using FluentAssertions;
namespace SB.FlowEngine.Tests;

// mock 业务执行器:按 moduleId 给 outputs,可记录调用次数
sealed class MockExec : IFlowNodeExecutor {
    public string ModuleId { get; }
    private readonly Func<FlowNodeContext, IReadOnlyDictionary<string, object?>> _fn;
    public int CallCount;
    public MockExec(string moduleId, Func<FlowNodeContext, IReadOnlyDictionary<string, object?>> fn)
        { ModuleId = moduleId; _fn = fn; }
    public Task<IReadOnlyDictionary<string, object?>> ExecuteAsync(FlowNodeContext ctx, CancellationToken ct = default)
        { CallCount++; return Task.FromResult(_fn(ctx)); }
}

public class ControlNodeTests {
    // 内置控制模块定义(端口对齐前端 builtin.ts)
    static ModuleDefinition IfMod() => new() { Id = "if",
        ExecInputs = { new(){Id="in"} },
        ExecOutputs = { new(){Id="true"}, new(){Id="false"} },
        DataInputs = { new(){Id="cond", Type="bool"} } };
    static ModuleDefinition LoopMod() => new() { Id = "loop",
        ExecInputs = { new(){Id="in"} },
        ExecOutputs = { new(){Id="body"}, new(){Id="done"} },
        DataInputs = { new(){Id="count", Type="number"} } };
    static ModuleDefinition GotoMod() => new() { Id="goto", ExecInputs={new(){Id="in"}}, ExecOutputs={new(){Id="to"}} };
    static ModuleDefinition LabelMod() => new() { Id="label", ExecInputs={new(){Id="in"}}, ExecOutputs={new(){Id="out"}} };
    static ModuleDefinition VarGetMod() => new() { Id="varGet", DataOutputs={new(){Id="value",Type="any"}} };
    static ModuleDefinition VarSetMod() => new() { Id="varSet", ExecInputs={new(){Id="in"}}, ExecOutputs={new(){Id="out"}}, DataInputs={new(){Id="value"}} };
    // 业务节点:cond 产 bool / emit 产数字 / sink 收集
    static ModuleDefinition SinkMod() => new() { Id="sink", ExecInputs={new(){Id="in"}}, ExecOutputs={new(){Id="out"}}, DataInputs={new(){Id="v",Type="any"}} };

    [Fact]
    public async Task If_true_branch() {
        var hit = new List<string>();
        var graph = new FlowGraph { Entry = "i",
            Nodes = {
                new(){Id="i", ModuleId="if", Config={["__"]=null}},
                new(){Id="t", ModuleId="sink"},
                new(){Id="f", ModuleId="sink"},
            },
            // cond 由一个 varGet 提供 true
            Variables = { new(){Name="c", Type="bool", Initial=true} },
        };
        graph.Nodes.Add(new(){Id="g", ModuleId="varGet", Config={["varName"]="c"}});
        graph.Edges.Add(new(){Id="d", Source="g", SourcePort="value", Target="i", TargetPort="cond", Kind="data"});
        graph.Edges.Add(new(){Id="et", Source="i", SourcePort="true", Target="t", TargetPort="in", Kind="exec"});
        graph.Edges.Add(new(){Id="ef", Source="i", SourcePort="false", Target="f", TargetPort="in", Kind="exec"});
        var mods = new List<ModuleDefinition> { IfMod(), VarGetMod(), SinkMod() };
        var execs = new List<IFlowNodeExecutor> {
            new MockExec("sink", ctx => { hit.Add("sink"); return new Dictionary<string,object?>(); }),
        };
        var sut = new FlowExecutor(execs);
        await sut.RunAsync(graph, mods);
        hit.Should().ContainSingle(); // 只走了 true 分支的那个 sink
    }

    [Fact]
    public async Task If_false_branch_when_cond_falsy() {
        // cond = 0 → falsy → 走 false 出口
        var which = "";
        var graph = new FlowGraph { Entry = "i",
            Variables = { new(){Name="c", Type="number", Initial=0} },
            Nodes = {
                new(){Id="i", ModuleId="if"},
                new(){Id="g", ModuleId="varGet", Config={["varName"]="c"}},
                new(){Id="t", ModuleId="sinkT"},
                new(){Id="f", ModuleId="sinkF"},
            },
            Edges = {
                new(){Id="d",Source="g",SourcePort="value",Target="i",TargetPort="cond",Kind="data"},
                new(){Id="et",Source="i",SourcePort="true",Target="t",TargetPort="in",Kind="exec"},
                new(){Id="ef",Source="i",SourcePort="false",Target="f",TargetPort="in",Kind="exec"},
            },
        };
        var mods = new List<ModuleDefinition> { IfMod(), VarGetMod(),
            new(){Id="sinkT", ExecInputs={new(){Id="in"}}, ExecOutputs={new(){Id="out"}}},
            new(){Id="sinkF", ExecInputs={new(){Id="in"}}, ExecOutputs={new(){Id="out"}}} };
        var execs = new List<IFlowNodeExecutor> {
            new MockExec("sinkT", _ => { which="T"; return new Dictionary<string,object?>(); }),
            new MockExec("sinkF", _ => { which="F"; return new Dictionary<string,object?>(); }),
        };
        await new FlowExecutor(execs).RunAsync(graph, mods);
        which.Should().Be("F");
    }

    [Fact]
    public async Task Loop_iterates_count_times_then_done() {
        int bodyCount = 0; bool doneHit = false;
        var graph = new FlowGraph { Entry = "lp",
            Variables = { new(){Name="n", Type="number", Initial=3} },
            Nodes = {
                new(){Id="lp", ModuleId="loop"},
                new(){Id="g", ModuleId="varGet", Config={["varName"]="n"}},
                new(){Id="b", ModuleId="body"},   // 循环体业务节点
                new(){Id="d", ModuleId="done"},   // 结束业务节点
            },
            Edges = {
                new(){Id="dc",Source="g",SourcePort="value",Target="lp",TargetPort="count",Kind="data"},
                new(){Id="eb",Source="lp",SourcePort="body",Target="b",TargetPort="in",Kind="exec"},
                new(){Id="back",Source="b",SourcePort="out",Target="lp",TargetPort="in",Kind="exec"}, // 回边
                new(){Id="ed",Source="lp",SourcePort="done",Target="d",TargetPort="in",Kind="exec"},
            },
        };
        var mods = new List<ModuleDefinition> { LoopMod(), VarGetMod(),
            new(){Id="body", ExecInputs={new(){Id="in"}}, ExecOutputs={new(){Id="out"}}},
            new(){Id="done", ExecInputs={new(){Id="in"}}, ExecOutputs={new(){Id="out"}}} };
        var execs = new List<IFlowNodeExecutor> {
            new MockExec("body", _ => { bodyCount++; return new Dictionary<string,object?>(); }),
            new MockExec("done", _ => { doneHit=true; return new Dictionary<string,object?>(); }),
        };
        await new FlowExecutor(execs).RunAsync(graph, mods);
        bodyCount.Should().Be(3);
        doneHit.Should().BeTrue();
    }

    [Fact]
    public async Task VarSet_writes_variable() {
        var graph = new FlowGraph { Entry = "s",
            Variables = { new(){Name="x", Type="number", Initial=0} },
            Nodes = {
                new(){Id="e", ModuleId="emit"},
                new(){Id="s", ModuleId="varSet", Config={["varName"]="x"}},
            },
            Edges = {
                new(){Id="d",Source="e",SourcePort="value",Target="s",TargetPort="value",Kind="data"},
                new(){Id="ee",Source="e",SourcePort="out",Target="s",TargetPort="in",Kind="exec"},
            },
        };
        // entry 是 emit(它先执行产出 42),再 exec 到 varSet
        graph.Entry = "e";
        var mods = new List<ModuleDefinition> { VarSetMod(),
            new(){Id="emit", ExecInputs={new(){Id="in"}}, ExecOutputs={new(){Id="out"}}, DataOutputs={new(){Id="value",Type="number"}}} };
        var execs = new List<IFlowNodeExecutor> {
            new MockExec("emit", _ => new Dictionary<string,object?>{["value"]=42}),
        };
        var r = await new FlowExecutor(execs).RunAsync(graph, mods);
        r.Variables["x"].Should().Be(42);
    }
}

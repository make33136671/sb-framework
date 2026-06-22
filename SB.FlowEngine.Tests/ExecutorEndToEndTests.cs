using SB.FlowEngine.Contracts;
using SB.FlowEngine.Execution;
using Xunit;
using FluentAssertions;
namespace SB.FlowEngine.Tests;

public class ExecutorEndToEndTests {
    static ModuleDefinition GotoMod() => new(){Id="goto", ExecInputs={new(){Id="in"}}, ExecOutputs={new(){Id="to"}}};

    [Fact]
    public async Task Unknown_module_throws() {
        var graph = new FlowGraph { Entry = "x",
            Nodes = { new(){Id="x", ModuleId="nope"} } };
        var mods = new List<ModuleDefinition> { new(){Id="nope", ExecOutputs={new(){Id="out"}}} };
        var act = async () => await new FlowExecutor(new List<IFlowNodeExecutor>()).RunAsync(graph, mods);
        (await act.Should().ThrowAsync<FlowExecutionException>()).WithMessage("*未知模块: nope*");
    }

    [Fact]
    public async Task MaxSteps_guard_throws() {
        // goto 自己指向自己 → 死循环
        var graph = new FlowGraph { Entry = "g",
            Nodes = { new(){Id="g", ModuleId="goto"} },
            Edges = { new(){Id="e",Source="g",SourcePort="to",Target="g",TargetPort="in",Kind="exec"} } };
        var mods = new List<ModuleDefinition> { GotoMod() };
        var act = async () => await new FlowExecutor(new List<IFlowNodeExecutor>())
            .RunAsync(graph, mods, new FlowExecutionOptions { MaxSteps = 50 });
        (await act.Should().ThrowAsync<FlowExecutionException>()).WithMessage("*执行步数超过上限 50*");
    }

    [Fact]
    public void Duplicate_moduleId_throws() {
        var execs = new List<IFlowNodeExecutor> {
            new MockExec("dup", _ => new Dictionary<string,object?>()),
            new MockExec("dup", _ => new Dictionary<string,object?>()),
        };
        var act = () => new FlowExecutor(execs);
        act.Should().Throw<FlowExecutionException>();
    }

    [Fact]
    public async Task Sequential_exec_order() {
        var order = new List<string>();
        var graph = new FlowGraph { Entry = "a",
            Nodes = { new(){Id="a",ModuleId="step"}, new(){Id="b",ModuleId="step"} },
            Edges = { new(){Id="e",Source="a",SourcePort="out",Target="b",TargetPort="in",Kind="exec"} } };
        var mods = new List<ModuleDefinition> { new(){Id="step", ExecInputs={new(){Id="in"}}, ExecOutputs={new(){Id="out"}}} };
        var execs = new List<IFlowNodeExecutor> {
            new MockExec("step", ctx => { order.Add("x"); return new Dictionary<string,object?>(); }) };
        var r = await new FlowExecutor(execs).RunAsync(graph, mods);
        order.Should().HaveCount(2);
        r.Steps.Should().Be(2);
    }
}

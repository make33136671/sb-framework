using SB.FlowEngine.Contracts;
using Xunit;
using FluentAssertions;
namespace SB.FlowEngine.Tests;

public class ContractTests {
    [Fact]
    public void Can_build_minimal_graph() {
        var g = new FlowGraph { Entry = "n1" };
        g.Nodes.Add(new FlowNode { Id = "n1", ModuleId = "emit" });
        g.Version.Should().Be("1.0");
        g.Nodes.Should().HaveCount(1);
    }
    [Fact]
    public void Module_ports_default_empty() {
        var m = new ModuleDefinition { Id = "if" };
        m.ExecOutputs.Should().BeEmpty();
        m.Config.Should().BeNull();
    }
}

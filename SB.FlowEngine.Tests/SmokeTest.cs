using Xunit;
using FluentAssertions;
namespace SB.FlowEngine.Tests;
public class SmokeTest {
    [Fact] public void Sanity() => (1 + 1).Should().Be(2);
}

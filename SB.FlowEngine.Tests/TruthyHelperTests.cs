using SB.FlowEngine.Execution;
using Xunit;
using FluentAssertions;
namespace SB.FlowEngine.Tests;

public class TruthyHelperTests {
    [Theory]
    [InlineData(null, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(0, false)]
    [InlineData(0.0, false)]
    [InlineData(1, true)]
    [InlineData(-1, true)]
    [InlineData("", false)]
    [InlineData("x", true)]
    [InlineData("false", true)]   // 非空字符串即 true(JS 语义)
    public void IsTruthy_matches_js(object? value, bool expected)
        => TruthyHelper.IsTruthy(value).Should().Be(expected);
}

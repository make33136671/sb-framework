using System.Text.Json;
using SB.FlowEngine.Contracts;
using SB.FlowEngine.Execution;
using Xunit;
using FluentAssertions;
namespace SB.FlowEngine.Tests;

/// <summary>
/// 真实场景:FlowGraph 来自前端 JSON 反序列化,所有 object? 字段(Config 值 / Variable.Initial)
/// 实际是 JsonElement。引擎必须能正确解包 JsonElement,否则 varGet/varSet 的 varName(as string→null)、
/// if 的 cond、loop 的 count 全失效。这是 C# 字面量构造图测不到的缺口。
/// </summary>
public class JsonGraphTests {
    static readonly JsonSerializerOptions Opts = new() { PropertyNameCaseInsensitive = true };

    static FlowGraph Deser(string json) => JsonSerializer.Deserialize<FlowGraph>(json, Opts)!;

    [Fact]
    public async Task If_cond_from_json_varGet_takes_true_branch() {
        var json = """
        {
          "version":"1.0","entry":"i",
          "variables":[{"name":"c","type":"bool","initial":true},{"name":"hit","type":"number","initial":0}],
          "nodes":[
            {"id":"i","moduleId":"if","config":{}},
            {"id":"g","moduleId":"varGet","config":{"varName":"c"}},
            {"id":"one","moduleId":"one","config":{}},
            {"id":"s","moduleId":"varSet","config":{"varName":"hit"}}
          ],
          "edges":[
            {"id":"d","source":"g","sourcePort":"value","target":"i","targetPort":"cond","kind":"data"},
            {"id":"e1","source":"i","sourcePort":"true","target":"one","targetPort":"in","kind":"exec"},
            {"id":"dv","source":"one","sourcePort":"value","target":"s","targetPort":"value","kind":"data"},
            {"id":"e2","source":"one","sourcePort":"out","target":"s","targetPort":"in","kind":"exec"}
          ]
        }
        """;
        var mods = new List<ModuleDefinition> {
            new(){Id="if",ExecInputs={new(){Id="in"}},ExecOutputs={new(){Id="true"},new(){Id="false"}},DataInputs={new(){Id="cond"}}},
            new(){Id="varGet",DataOutputs={new(){Id="value"}}},
            new(){Id="varSet",ExecInputs={new(){Id="in"}},ExecOutputs={new(){Id="out"}},DataInputs={new(){Id="value"}}},
            new(){Id="one",ExecInputs={new(){Id="in"}},ExecOutputs={new(){Id="out"}},DataOutputs={new(){Id="value"}}},
        };
        var execs = new IFlowNodeExecutor[] { new MockExec("one", _ => new Dictionary<string,object?>{["value"]=1}) };
        var r = await new FlowExecutor(execs).RunAsync(Deser(json), mods);
        // hit 应被 varSet 写成 1(走了 true 分支 + varSet 生效)
        Convert.ToInt32(r.Variables["hit"]).Should().Be(1);
    }

    [Fact]
    public async Task Loop_count_from_json_iterates() {
        var json = """
        {
          "version":"1.0","entry":"lp",
          "variables":[{"name":"n","type":"number","initial":3}],
          "nodes":[
            {"id":"lp","moduleId":"loop","config":{}},
            {"id":"g","moduleId":"varGet","config":{"varName":"n"}},
            {"id":"b","moduleId":"body","config":{}},
            {"id":"d","moduleId":"done","config":{}}
          ],
          "edges":[
            {"id":"dc","source":"g","sourcePort":"value","target":"lp","targetPort":"count","kind":"data"},
            {"id":"eb","source":"lp","sourcePort":"body","target":"b","targetPort":"in","kind":"exec"},
            {"id":"back","source":"b","sourcePort":"out","target":"lp","targetPort":"in","kind":"exec"},
            {"id":"ed","source":"lp","sourcePort":"done","target":"d","targetPort":"in","kind":"exec"}
          ]
        }
        """;
        var mods = new List<ModuleDefinition> {
            new(){Id="loop",ExecInputs={new(){Id="in"}},ExecOutputs={new(){Id="body"},new(){Id="done"}},DataInputs={new(){Id="count"}}},
            new(){Id="varGet",DataOutputs={new(){Id="value"}}},
            new(){Id="body",ExecInputs={new(){Id="in"}},ExecOutputs={new(){Id="out"}}},
            new(){Id="done",ExecInputs={new(){Id="in"}},ExecOutputs={new(){Id="out"}}},
        };
        int bodyCount = 0;
        var execs = new IFlowNodeExecutor[] {
            new MockExec("body", _ => { bodyCount++; return new Dictionary<string,object?>(); }),
            new MockExec("done", _ => new Dictionary<string,object?>()),
        };
        await new FlowExecutor(execs).RunAsync(Deser(json), mods);
        bodyCount.Should().Be(3);
    }

    [Fact]
    public void IsTruthy_handles_json_element() {
        var t = JsonSerializer.Deserialize<JsonElement>("true");
        var f = JsonSerializer.Deserialize<JsonElement>("false");
        var zero = JsonSerializer.Deserialize<JsonElement>("0");
        var emptyStr = JsonSerializer.Deserialize<JsonElement>("\"\"");
        var nullEl = JsonSerializer.Deserialize<JsonElement>("null");
        TruthyHelper.IsTruthy(t).Should().BeTrue();
        TruthyHelper.IsTruthy(f).Should().BeFalse();
        TruthyHelper.IsTruthy(zero).Should().BeFalse();
        TruthyHelper.IsTruthy(emptyStr).Should().BeFalse();
        TruthyHelper.IsTruthy(nullEl).Should().BeFalse();
    }
}

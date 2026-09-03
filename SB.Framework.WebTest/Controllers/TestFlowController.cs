using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SB.FlowEngine.Contracts;
using SB.FlowEngine.Execution;
using SB.Framework.Models;

namespace SB.Framework.WebTest.Controllers;

/// <summary>
/// FlowEngine 图执行场景:两个真实业务流程模拟 + 自定义业务节点扩展点演示。
/// 引擎语义:执行指针沿 exec 线走,数据沿 data 线经 varGet/上游输出拉取;
/// 业务节点沿其第一个 exec 出口推进,分支判断交给内置 if 节点。
/// </summary>
[ApiController, Route("/test/flow/[action]")]
public class TestFlowController : ControllerBase
{
    // 图 JSON 为 camelCase,与前端 @saibo/flow-engine 同构;反序列化必须大小写不敏感,
    // 否则 Entry/Nodes/Edges 全部绑不上,引擎拿到空图(steps=0)
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// 场景一:订单风控审批 —— 金额阈值分流(真实"下单后风控"业务):
    /// riskCheck 业务节点产出 passed/reason → if 按 passed 分支 → varSet 落审批结论。
    /// amount ≥ 1000 转人工,否则自动通过。
    /// </summary>
    [HttpPost, AllowAnonymous]
    public async Task<Result> RiskApproval([FromForm] decimal amount = 1500)
    {
        var graph = JsonSerializer.Deserialize<FlowGraph>($$"""
            {
              "version": "1.0",
              "entry": "risk",
              "variables": [
                { "name": "amount", "type": "number", "initial": {{amount}} },
                { "name": "verdict", "type": "string", "initial": "" }
              ],
              "nodes": [
                { "id": "get",  "moduleId": "varGet",    "config": { "varName": "amount" } },
                { "id": "risk", "moduleId": "riskCheck", "config": { "threshold": 1000 } },
                { "id": "gate", "moduleId": "if",        "config": {} },
                { "id": "auto", "moduleId": "varSet",    "config": { "varName": "verdict" } },
                { "id": "hold", "moduleId": "varSet",    "config": { "varName": "verdict" } }
              ],
              "edges": [
                { "id": "d0",  "kind": "data", "source": "get",  "sourcePort": "value",  "target": "risk", "targetPort": "amount" },
                { "id": "d1",  "kind": "data", "source": "risk", "sourcePort": "passed", "target": "gate", "targetPort": "cond" },
                { "id": "d2",  "kind": "data", "source": "risk", "sourcePort": "reason", "target": "auto", "targetPort": "value" },
                { "id": "d3",  "kind": "data", "source": "risk", "sourcePort": "reason", "target": "hold", "targetPort": "value" },
                { "id": "e1",  "kind": "exec", "source": "risk", "sourcePort": "out",    "target": "gate", "targetPort": "in" },
                { "id": "e2",  "kind": "exec", "source": "gate", "sourcePort": "true",   "target": "auto", "targetPort": "in" },
                { "id": "e3",  "kind": "exec", "source": "gate", "sourcePort": "false",  "target": "hold", "targetPort": "in" }
              ]
            }
            """, JsonOpts)!;
        var modules = new List<ModuleDefinition>
        {
            new()
            {
                Id = "riskCheck",
                ExecInputs = { new() { Id = "in" } },
                ExecOutputs = { new() { Id = "out" } },
                DataInputs = { new() { Id = "amount" } },
                DataOutputs = { new() { Id = "passed" }, new() { Id = "reason" } },
            },
            new()
            {
                Id = "if",
                ExecInputs = { new() { Id = "in" } },
                ExecOutputs = { new() { Id = "true" }, new() { Id = "false" } },
                DataInputs = { new() { Id = "cond" } },
            },
            new()
            {
                Id = "varSet",
                ExecInputs = { new() { Id = "in" } },
                ExecOutputs = { new() { Id = "out" } },
                DataInputs = { new() { Id = "value" } },
            },
        };
        var expect = amount >= 1000 ? "转人工审批" : "自动通过";
        return await RunAsync(graph, modules, "风控审批流", $"verdict = '{expect}'");
    }

    /// <summary>
    /// 场景二:批量发货管道 —— loop 固定圈数循环,每圈 shipOne 业务节点扣减库存变量,
    /// wait 模拟发货耗时。模拟"从队列循环取单发货"的数据管道业务。
    /// </summary>
    [HttpPost, AllowAnonymous]
    public async Task<Result> ShipPipeline([FromForm] int orderCount = 3)
    {
        var graph = JsonSerializer.Deserialize<FlowGraph>($$"""
            {
              "version": "1.0",
              "entry": "loop",
              "variables": [
                { "name": "stock", "type": "number", "initial": {{orderCount}} }
              ],
              "nodes": [
                { "id": "loop", "moduleId": "loop",    "config": { "count": {{orderCount}} } },
                { "id": "get",  "moduleId": "varGet",  "config": { "varName": "stock" } },
                { "id": "ship", "moduleId": "shipOne", "config": {} },
                { "id": "dec",  "moduleId": "varSet",  "config": { "varName": "stock" } },
                { "id": "wait", "moduleId": "wait",    "config": { "durationMs": 10 } }
              ],
              "edges": [
                { "id": "d1",  "kind": "data", "source": "get",  "sourcePort": "value", "target": "ship", "targetPort": "stock" },
                { "id": "d2",  "kind": "data", "source": "ship", "sourcePort": "left",  "target": "dec",  "targetPort": "value" },
                { "id": "e1",  "kind": "exec", "source": "loop", "sourcePort": "body",  "target": "ship", "targetPort": "in" },
                { "id": "e2",  "kind": "exec", "source": "ship", "sourcePort": "out",   "target": "dec",  "targetPort": "in" },
                { "id": "e3",  "kind": "exec", "source": "dec",  "sourcePort": "out",   "target": "wait", "targetPort": "in" },
                { "id": "e4",  "kind": "exec", "source": "wait", "sourcePort": "out",   "target": "loop", "targetPort": "in" }
              ]
            }
            """, JsonOpts)!;
        var modules = new List<ModuleDefinition>
        {
            new()
            {
                Id = "loop",
                ExecInputs = { new() { Id = "in" } },
                ExecOutputs = { new() { Id = "body" }, new() { Id = "done" } },
                DataInputs = { new() { Id = "count" } },
            },
            new()
            {
                Id = "shipOne",
                ExecInputs = { new() { Id = "in" } },
                ExecOutputs = { new() { Id = "out" } },
                DataInputs = { new() { Id = "stock" } },
                DataOutputs = { new() { Id = "left" } },
            },
            new()
            {
                Id = "varSet",
                ExecInputs = { new() { Id = "in" } },
                ExecOutputs = { new() { Id = "out" } },
                DataInputs = { new() { Id = "value" } },
            },
        };
        return await RunAsync(graph, modules, "发货管道流(loop)",
            $"最终 stock = 0(共发货 {orderCount} 件)");
    }

    private async Task<Result> RunAsync(FlowGraph graph, List<ModuleDefinition> modules, string scene, string expect)
    {
        var executor = new FlowExecutor(WebTestFlowNodes.AllBuiltins());
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = await executor.RunAsync(graph, modules);
        sw.Stop();
        return Result.Success($"{scene} 执行完成({sw.ElapsedMilliseconds}ms),预期 {expect}", new
        {
            FinalVariables = result.Variables,
            Steps = result.Steps,
        });
    }
}

/// <summary>
/// 自定义业务节点(IFlowNodeExecutor 扩展点演示):真实业务节点就按这个姿势写。
/// riskCheck 风控分流 / shipOne 模拟发货扣库存。
/// </summary>
public static class WebTestFlowNodes
{
    public static IEnumerable<IFlowNodeExecutor> AllBuiltins() =>
        [new RiskCheckNode(), new ShipOneNode()];

    private sealed class RiskCheckNode : IFlowNodeExecutor
    {
        public string ModuleId => "riskCheck";

        public Task<IReadOnlyDictionary<string, object?>> ExecuteAsync(FlowNodeContext ctx, CancellationToken ct = default)
        {
            var threshold = Convert.ToDecimal(
                ctx.Config.TryGetValue("threshold", out var t) ? Convert.ToDecimal(ToDecimal(t)) : 1000m);
            var amount = Convert.ToDecimal(
                ctx.Inputs.TryGetValue("amount", out var a) ? ToDecimal(a) : 0m);
            var passed = amount < threshold;
            var reason = passed
                ? $"金额 {amount} < 阈值 {threshold},自动通过"
                : $"金额 {amount} ≥ 阈值 {threshold},转人工审批";
            return Task.FromResult<IReadOnlyDictionary<string, object?>>(
                new Dictionary<string, object?> { ["passed"] = passed, ["reason"] = reason });
        }

        private static decimal ToDecimal(object? v) => v switch
        {
            null => 0m,
            decimal d => d,
            JsonElement je => je.ValueKind == JsonValueKind.Number ? je.GetDecimal()
                : decimal.TryParse(je.ToString(), out var m) ? m : 0m,
            _ => decimal.TryParse(v.ToString(), out var m) ? m : 0m,
        };
    }

    private sealed class ShipOneNode : IFlowNodeExecutor
    {
        public string ModuleId => "shipOne";

        public Task<IReadOnlyDictionary<string, object?>> ExecuteAsync(FlowNodeContext ctx, CancellationToken ct = default)
        {
            // Config/Inputs 的值可能是 JsonElement(图经 JSON 反序列化),须显式处理
            var stock = ToInt(ctx.Inputs.TryGetValue("stock", out var s) ? s : 0);
            return Task.FromResult<IReadOnlyDictionary<string, object?>>(
                new Dictionary<string, object?> { ["left"] = Math.Max(0, stock - 1) });
        }

        private static int ToInt(object? value) => value switch
        {
            null => 0,
            int i => i,
            long l => (int)l,
            double d => (int)d,
            decimal m => (int)m,
            JsonElement je => je.ValueKind == JsonValueKind.Number ? je.GetInt32()
                : int.TryParse(je.ToString(), out var n) ? n : 0,
            _ => int.TryParse(value.ToString(), out var n) ? n : 0,
        };
    }
}

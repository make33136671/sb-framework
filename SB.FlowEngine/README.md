# SB.FlowEngine

SB 框架的图执行引擎(前端 @saibo/flow-engine 的服务端对称实现)。读 FlowGraph 在服务端执行,内置 if/loop/goto/label/varGet/varSet 控制节点,业务节点经 IFlowNodeExecutor 接入。

## 用法

1. 业务层为每个业务节点实现 IFlowNodeExecutor:
   ```csharp
   public class WechatQueryExecutor : IFlowNodeExecutor {
       public string ModuleId => "wechatQuery";
       public async Task<IReadOnlyDictionary<string, object?>> ExecuteAsync(
           FlowNodeContext ctx, CancellationToken ct = default) {
           var phone = ctx.Inputs["phone"];
           // ...业务逻辑...
           return new Dictionary<string, object?> { ["result"] = "..." };
       }
   }
   ```
2. 构造引擎并执行:
   ```csharp
   var executor = new FlowExecutor(allExecutors);   // allExecutors 由 DI 集合注入
   var result = await executor.RunAsync(graph, modules);
   // result.Variables / result.Steps
   ```

## 与前端的契约对齐

FlowGraph/执行语义/错误文案严格对齐前端 @saibo/flow-engine,同一张图前后端跑出相同结果。详见 docs/CONTRACT.md(前端仓库)。

## 边界

引擎只提供:图执行 + 契约类型 + IFlowNodeExecutor 接口 + 内置控制节点。
业务节点实现、Controller、图持久化由业务层负责。

# SB.FlowEngine 设计文档

日期：2026-06-22
类型：SB 框架附带功能 lib（.NET / NuGet，发布到 baget Mirror）

## 1. 目标与定位

`SB.FlowEngine` 是一个**图执行引擎库**，作为 SB 框架的附带功能，做成独立 NuGet lib（为了升级方便、给框架瘦身、按需引用）。它是前端 `@saibo/flow-engine`（可视化编排引擎）的**服务端对称实现**——让前端画出的图能在服务端真实执行。

### 核心定位

- **专为 SB 设计**：依赖 `SB.Framework.Core`，内部需要的能力直接用 SB.Core，不自造，不考虑非 SB 兼容。它本来就是 SB 的一部分。
- **一个包全有**：引用 `SB.FlowEngine` 即得到——图执行引擎 + 数据契约类型 + 节点执行接口 + 内置控制节点。
- **引擎机制，不含业务**：业务节点实现、Controller、持久化都在业务层（调用者自己写），不在本库。
- **严格对齐前端契约**：FlowGraph 结构、执行语义、错误文案与前端 `@saibo/flow-engine` 一致——同一张图前后端跑出相同结果。

### 长远

以后取代 avm 现有的 `AutomationService`（扁平步骤引擎）。**本次只做引擎库本身，不动 avm，不动前端。**

## 2. 架构与包结构

```
SB.FlowEngine/                      ← 引擎层 lib(依赖 SB.Framework.Core)
├── Contracts/                      契约类型(对齐前端 contract.ts)
│   FlowGraph / FlowNode / FlowEdge / FlowVariable
│   ModuleDefinition / ModulePort / ModuleConfigField / TypeDefinition
├── Execution/
│   IFlowNodeExecutor               业务节点执行接口(业务层实现)
│   FlowNodeContext                 执行上下文(ModuleId/Config/Inputs)
│   FlowExecutor                    图执行引擎(执行指针+数据拉取+终止保护)
│   FlowExecutionOptions            选项(MaxSteps/OnNodeStatus)
│   FlowExecutionResult             返回(Variables + Steps)
│   FlowExecutionException          引擎异常(文案对齐前端)
└── Builtin/
    内置控制节点执行逻辑(if/loop/goto/label/varGet/varSet)

业务层(如 avm,调用者自己写,不在本库):
  - 实现各 IFlowNodeExecutor(微信查询/点击等业务节点)
  - Controller(接收图 / 触发执行 / 查执行记录)
  - 图的持久化(如需)
```

**职责边界**：引擎层 = 引擎机制 + 契约 + 接口 + 内置控制节点；业务层 = 节点实现 + Controller + 持久化。

**依赖**：`SB.Framework.Core`（开发期 ProjectReference，`dotnet pack` 自动转 PackageReference，按 framework 惯例）。`SB.Framework.Models` 仅在确需共享实体/枚举时引（首版预计不需）。

**项目位置**：`framework/sb-framework/SB.FlowEngine/`，与 `SB.Feature.*` / `SB.Framework.Core` 同级（同一 git 仓库、同一 `sb-framework.sln`）。

**测试**：`framework/sb-framework/SB.FlowEngine.Tests/`，xUnit（对称前端 Vitest）。

## 3. 契约类型（对齐前端 contract.ts）

C# 类型一一对应前端 TS 接口。字段名遵循 C# 习惯（PascalCase），JSON 序列化用 SB.Core 约定映射到前端的 camelCase（前后端 JSON 互通）。

```csharp
public sealed class TypeDefinition {
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public string Color { get; set; } = "";
}

public sealed class ModulePort {
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public string? Type { get; set; }        // 仅 data 端口有
    public bool Required { get; set; }
}

public sealed class ModuleConfigField {
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public string Widget { get; set; } = ""; // text/number/select/switch
    public List<ConfigOption>? Options { get; set; }
}
public sealed class ConfigOption { public string Label {get;set;}=""; public object? Value {get;set;} }

public sealed class ModuleDefinition {
    public string Id { get; set; } = "";
    public string Category { get; set; } = "";
    public string Label { get; set; } = "";
    public string? Icon { get; set; }
    public List<ModulePort> ExecInputs { get; set; } = new();
    public List<ModulePort> ExecOutputs { get; set; } = new();
    public List<ModulePort> DataInputs { get; set; } = new();
    public List<ModulePort> DataOutputs { get; set; } = new();
    public List<ModuleConfigField>? Config { get; set; }
}

public sealed class FlowNode {
    public string Id { get; set; } = "";
    public string ModuleId { get; set; } = "";
    public NodePosition Position { get; set; } = new();
    public Dictionary<string, object?> Config { get; set; } = new();
}
public sealed class NodePosition { public double X {get;set;} public double Y {get;set;} }

public sealed class FlowEdge {
    public string Id { get; set; } = "";
    public string Source { get; set; } = "";
    public string SourcePort { get; set; } = "";
    public string Target { get; set; } = "";
    public string TargetPort { get; set; } = "";
    public string Kind { get; set; } = "";   // "exec" | "data"
}

public sealed class FlowVariable {
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public object? Initial { get; set; }
}

public sealed class FlowGraph {
    public string Version { get; set; } = "1.0";
    public List<FlowNode> Nodes { get; set; } = new();
    public List<FlowEdge> Edges { get; set; } = new();
    public List<FlowVariable> Variables { get; set; } = new();
    public string Entry { get; set; } = "";
}
```

## 4. 执行引擎语义（严格对齐前端 executor.ts）

C# 行为必须与前端执行器逐项一致——同一张图前后端跑出同样结果。

### 4.1 主流程（FlowExecutor.RunAsync）

- 从 `graph.Entry` 起，**执行指针(pc)沿 exec 线推进**：执行当前节点 → 沿其执行出口连线找下一节点 → 继续，直到无出口（pc 为 null）终止
- 初始化：全局变量 `variables[v.Name] = v.Initial`；业务节点输出缓存 `nodeOutputs`（只在 exec 链走到该节点时填充）；循环计数 `loopCounters`（按节点 id 独立）
- 每步前 `steps++`，超 `MaxSteps`(默认 100000) 抛 `FlowExecutionException`

### 4.2 数据拉取（GatherInputs）

节点要数据输入时，遍历 `Kind=="data" && Target==当前节点` 的边，沿 data 线向上游取值：

| 上游节点 | 取值方式 |
|---|---|
| varGet | 直接读 `variables[上游.Config["varName"]]` |
| 业务节点(非控制) | 读 `nodeOutputs[上游id][sourcePort]`；缓存不存在 → 抛错（文案见 4.6） |
| 其他控制节点 | 忽略 |

### 4.3 内置控制节点（端口 id 对齐前端 builtin.ts）

CONTROL_IDS = { "if", "loop", "goto", "label", "varGet", "varSet" }，这些节点引擎内置处理，不调 IFlowNodeExecutor、不写 nodeOutputs。

| 节点 | 端口 | 行为 |
|---|---|---|
| **if** | in / true / false / cond(data,bool) | 读 cond，**truthy** 走 true 出口，否则 false 出口 |
| **loop** | in / body / done / count(data,number) | count = `Inputs["count"] ?? Config["count"] ?? 0`(经数值转换)；done<count 走 body(计数+1)，否则走 done(清计数) |
| **goto** | in / to | 走 to 出口 |
| **label** | in / out | 走 out 出口 |
| **varSet** | in / out / value(data) | `variables[Config["varName"]] = Inputs["value"]`，走 out 出口 |
| **varGet** | value(data,out) | 纯数据节点，无 exec 口，不进 exec 主循环；仅在数据拉取时直读变量 |

### 4.4 业务节点执行

- 按 moduleId 找 IFlowNodeExecutor；找不到 → 抛错（"未知模块: {moduleId}"）
- 拉输入 → `executor.ExecuteAsync(ctx, ct)` → 输出存 `nodeOutputs[节点id]`
- **沿 ExecOutputs[0]（第一个执行出口）走**（不是硬编码 "out"）；若该节点 ExecOutputs 为空，pc=null 终止

### 4.5 关键对齐点（C# 易踩坑，实现必须处理）

1. **truthy 语义**：if 的 cond 按 **JS truthy** 判定（`null` / `false` / `0` / `0.0` / `""` / 空 → false；其余 → true）。C# 不能直接 `(bool)`，需实现一个 `IsTruthy(object?)` 助手对齐 JS。**必须单测覆盖 0/""/null 走假分支。**
2. **缓存只读不重跑**：业务节点只在 exec 链执行一次，输出进缓存；数据拉取只读缓存，**绝不重新执行业务节点**（前端踩过这个坑，会导致有副作用的业务节点被多执行）。**必须单测：业务节点只执行一次。**
3. **loop count 数值转换**：count 来源 data 入口或 config，可能是各种 JSON 数值类型，统一转 int（对齐前端 `Number()`）。
4. **错误文案逐字对齐前端**（4.6），便于前后端联调对照。

### 4.6 错误文案（与前端一字一致）

- 上游未执行：`节点 {nodeId} 的输出在被读取前尚未执行，请检查执行顺序`
- 超步：`执行步数超过上限 {maxSteps}(可能存在死循环)`
- 未知模块：`未知模块: {moduleId}`

### 4.7 返回

```csharp
public sealed class FlowExecutionResult {
    public IReadOnlyDictionary<string, object?> Variables { get; init; }
    public int Steps { get; init; }
}
```

## 5. 节点执行接口（引擎↔业务衔接）

```csharp
public sealed class FlowNodeContext {
    public string ModuleId { get; init; } = "";
    public IReadOnlyDictionary<string, object?> Config { get; init; }
        = new Dictionary<string, object?>();
    public IReadOnlyDictionary<string, object?> Inputs { get; init; }
        = new Dictionary<string, object?>();
}

public interface IFlowNodeExecutor {
    string ModuleId { get; }   // 处理哪个 moduleId
    Task<IReadOnlyDictionary<string, object?>> ExecuteAsync(
        FlowNodeContext ctx, CancellationToken ct = default);
}

public sealed class FlowExecutor {
    public FlowExecutor(IEnumerable<IFlowNodeExecutor> executors, ILogger? logger = null);
    public Task<FlowExecutionResult> RunAsync(
        FlowGraph graph, FlowExecutionOptions? options = null, CancellationToken ct = default);
}

public sealed class FlowExecutionOptions {
    public int MaxSteps { get; set; } = 100_000;
    public Action<string, string>? OnNodeStatus { get; set; } // (nodeId, "running"|"success"|"failed")
}
```

- 引擎构造时收 `IEnumerable<IFlowNodeExecutor>`，按 `ModuleId` 建字典分发（重复 ModuleId 抛配置错）
- 业务层（avm 等）每个业务节点实现一个 IFlowNodeExecutor，经 SB DI（`[AutoDependencyInjection]` 或集合注入）自动被引擎拿到
- 数据值统一用 `object?`（对齐前端 JSON 动态值）

## 6. 测试策略（引擎质量保证，对称前端）

xUnit，纯逻辑单测，mock IFlowNodeExecutor 模拟业务节点（不依赖真实设备/框架运行时）：

- **控制节点各场景**：if 真/假分支、loop 迭代 N 轮+走 done、goto/label 跳转、varGet/varSet 读写
- **数据拉取**：业务节点输出传下游、varGet 直读变量、**上游未执行被读抛对应错**
- **业务节点只执行一次**（缓存不重跑——必测）
- **truthy 语义**：cond 为 0 / 空串 / null 走假分支（C# 对齐 JS 的关键——必测）
- **终止保护**：死循环超 MaxSteps 抛错
- **端到端**：mock 执行器跑完整图，断言执行顺序 + 最终变量
- **未知模块 / 重复 ModuleId** 抛错

## 7. 错误处理

- 引擎级错误（未知模块/上游未执行/超步）→ 抛 `FlowExecutionException`，文案对齐前端
- 业务节点 `ExecuteAsync` 抛异常 → 引擎触发 `OnNodeStatus(failed)` 后向上抛（由调用方决定处理，对齐前端）

## 8. 本次范围

**做**：
- `SB.FlowEngine` 包：契约类型 + FlowExecutor + IFlowNodeExecutor + FlowNodeContext + 内置控制节点 + 异常/选项/结果类型
- `SB.FlowEngine.Tests`：上述测试
- 发布到 baget Mirror

**不做（YAGNI / 业务层职责）**：
- 业务节点实现、Controller、图的持久化（业务层）
- 不动 avm 的 AutomationService（取代是后话）
- 前端不动
- 图合法性校验（连线类型/环检测）——首版由前端保证，后端只执行；如需服务端校验留后续
- 图的 ModuleDefinition/TypeDefinition 校验执行——首版执行器不强依赖 ModuleDefinition（除业务节点的 ExecOutputs[0] 需要知道出口；这点通过 graph 的 edge 即可推断，或要求调用方传 modules。实现时确认：执行器是否需要 modules 列表？前端执行器用 modules 取 execOutputs[0]，C# 同样需要——所以 RunAsync 需能拿到节点的 ExecOutputs，方案见下）

## 9. 待实现时定的一个细节（不影响设计批准）

前端执行器靠 `modules`（ModuleDefinition 列表）取业务节点的 `execOutputs[0]`。C# 侧同样需要知道每个业务节点的执行出口。两个方案，实现时择优：
- **A**：`RunAsync` 额外收 `IReadOnlyList<ModuleDefinition> modules`，按 moduleId 查 ExecOutputs[0]（与前端完全对称，推荐）
- **B**：从 graph.edges 推断该节点唯一的 exec 出口（无需 modules，但多出口业务节点会有歧义）

倾向 A（对称前端、无歧义）。此为实现细节，在实现计划中定。

## 10. 风险

- **前后端语义不一致**：以"错误文案逐字对齐 + truthy 助手 + 缓存只读"三条硬规则 + 充分单测守住。
- **JSON 类型映射**：C# `object?` ↔ 前端 JSON 动态值，数值/布尔/字符串/null 的序列化要和前端 JSON 对齐（用 SB.Core 的序列化约定）。
- **取代 AutomationService 的迁移**（远期）：本次不涉及，但契约设计已为之留好（引擎通用、业务节点可插拔）。

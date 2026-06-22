# SB.FlowEngine 实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 实现 SB.FlowEngine 图执行引擎 lib——前端 @saibo/flow-engine 的服务端对称实现,让前端画的图能在 SB 服务端真实执行,发布到 baget。

**Architecture:** 一个 .NET 库(依赖 SB.Framework.Core),含契约类型 + FlowExecutor(执行指针沿exec线走+数据拉取只读缓存) + IFlowNodeExecutor 接口(业务层实现节点) + 内置控制节点(if/loop/goto/label/varGet/varSet)。严格对齐前端执行语义。纯逻辑用 xUnit TDD,业务节点用 mock 执行器测。

**Tech Stack:** .NET 10, C#, xUnit + FluentAssertions(对称前端 Vitest), 发布 baget Mirror。

## 设计文档

`framework/sb-framework/docs/superpowers/specs/2026-06-22-sb-flowengine-design.md`(完整蓝本,实现前先读)。

## 前端对照源(行为必须一致,实现时对照)

- `/Users/tianlong/projects/saibo/apps/flow-engine/src/types/contract.ts`(契约)
- `/Users/tianlong/projects/saibo/apps/flow-engine/src/executor/executor.ts`(执行器语义)
- `/Users/tianlong/projects/saibo/apps/flow-engine/src/nodes/builtin.ts`(内置控制节点端口id)
- `/Users/tianlong/projects/saibo/apps/flow-engine/docs/CONTRACT.md`(契约文档)

## Global Constraints

- 项目位置 `framework/sb-framework/SB.FlowEngine/`,测试 `framework/sb-framework/SB.FlowEngine.Tests/`,加入 `sb-framework.sln`
- TargetFramework `net10.0`;PackageId `SB.FlowEngine`;依赖 SB.Framework.Core(开发期 ProjectReference,pack 自动转包)
- 严格对齐前端契约:FlowGraph 结构、执行语义、**错误文案逐字一致**(见下)
- 错误文案(与前端一字不差):
  - 上游未执行: `节点 {nodeId} 的输出在被读取前尚未执行，请检查执行顺序`
  - 超步: `执行步数超过上限 {maxSteps}(可能存在死循环)`
  - 未知模块: `未知模块: {moduleId}`
- **truthy 语义对齐 JS**: null/false/0/0.0/""/空 → false,其余 → true(用 IsTruthy 助手)
- **缓存只读不重跑**: 业务节点只在 exec 链执行一次,数据拉取只读 nodeOutputs 缓存
- CONTROL_IDS = { "if","loop","goto","label","varGet","varSet" }
- 业务节点沿 ExecOutputs[0](第一个执行出口)走,不硬编码 "out";执行器 RunAsync 收 modules 列表以查 ExecOutputs[0](方案A,对称前端)
- 测试 xUnit + FluentAssertions;纯逻辑必须单测真断言;提交中文 Conventional Commit,显式 git add,gpg 失败用 `git -c commit.gpgsign=false commit`;不 push(用户统一处理)
- 数据值统一 `object?`(对齐前端 JSON 动态值)

## 文件结构

```
SB.FlowEngine/
  SB.FlowEngine.csproj
  Contracts/
    FlowGraph.cs        (FlowGraph/FlowNode/FlowEdge/FlowVariable/NodePosition)
    ModuleDefinition.cs (ModuleDefinition/ModulePort/ModuleConfigField/ConfigOption/TypeDefinition)
  Execution/
    FlowNodeContext.cs
    IFlowNodeExecutor.cs
    FlowExecutionOptions.cs
    FlowExecutionResult.cs
    FlowExecutionException.cs
    TruthyHelper.cs
    FlowExecutor.cs
SB.FlowEngine.Tests/
  SB.FlowEngine.Tests.csproj
  TruthyHelperTests.cs
  ControlNodeTests.cs
  DataPullTests.cs
  ExecutorEndToEndTests.cs
```

---

## Task 1: 项目脚手架 + 加入 sln

**Files:**
- Create: `SB.FlowEngine/SB.FlowEngine.csproj`
- Create: `SB.FlowEngine/Placeholder.cs`(临时,确保能编译,后续删)
- Create: `SB.FlowEngine.Tests/SB.FlowEngine.Tests.csproj`
- Modify: `sb-framework.sln`

**Interfaces:**
- Produces: 可 `dotnet build` / `dotnet test` 的空骨架,SB.FlowEngine 引用 SB.Framework.Core

- [ ] **Step 1: 写 SB.FlowEngine.csproj**

参照 `SB.Feature.LicenseManage/SB.Feature.LicenseManage.csproj` 的打包配置:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace>SB.FlowEngine</RootNamespace>
    <AssemblyName>SB.FlowEngine</AssemblyName>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <GeneratePackageOnBuild>true</GeneratePackageOnBuild>
    <PackageId>SB.FlowEngine</PackageId>
    <Version>0.1.0</Version>
    <Description>SB 图执行引擎(前端 @saibo/flow-engine 的服务端对称实现):读 FlowGraph 执行,内置 if/loop/goto/变量 控制节点,业务节点经 IFlowNodeExecutor 接入</Description>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\SB.Framework.Core\SB.Framework.Core.csproj" />
  </ItemGroup>
</Project>
```
> 注:先确认 SB.Framework.Core.csproj 的相对路径正确(它和 SB.FlowEngine 同在 framework/sb-framework/ 下,所以 `..\SB.Framework.Core\` 对)。

- [ ] **Step 2: 写临时 Placeholder.cs**

```csharp
namespace SB.FlowEngine;
internal static class Placeholder { }
```

- [ ] **Step 3: 写测试项目 csproj**

参照 `SB.Chat.Tests/SB.Chat.Tests.csproj`,但 TargetFramework 用 net10.0,加 FluentAssertions:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
    <PackageReference Include="xunit" Version="2.6.6" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.5.6" />
    <PackageReference Include="FluentAssertions" Version="6.12.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\SB.FlowEngine\SB.FlowEngine.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 4: 加入 sln**

Run:
```bash
cd /Users/tianlong/projects/saibo/framework/sb-framework
dotnet sln sb-framework.sln add SB.FlowEngine/SB.FlowEngine.csproj SB.FlowEngine.Tests/SB.FlowEngine.Tests.csproj
```
Expected: 两个项目加入成功

- [ ] **Step 5: 写一个占位测试确保测试项目能跑**

`SB.FlowEngine.Tests/SmokeTest.cs`:
```csharp
using Xunit;
using FluentAssertions;
namespace SB.FlowEngine.Tests;
public class SmokeTest {
    [Fact] public void Sanity() => (1 + 1).Should().Be(2);
}
```

- [ ] **Step 6: 编译 + 测试**

Run:
```bash
cd /Users/tianlong/projects/saibo/framework/sb-framework
dotnet build SB.FlowEngine/SB.FlowEngine.csproj -nologo 2>&1 | grep -E "错误|个错误|已成功生成" | head
dotnet test SB.FlowEngine.Tests/SB.FlowEngine.Tests.csproj -nologo 2>&1 | tail -3
```
Expected: 库 0 错误;测试 1 passed
> 若 SB.Framework.Core 依赖链拉入别的包导致还原失败,看报错——可能需确认 NuGet.config(framework 目录有)指向 baget。

- [ ] **Step 7: 提交**

```bash
git add SB.FlowEngine/SB.FlowEngine.csproj SB.FlowEngine/Placeholder.cs SB.FlowEngine.Tests/SB.FlowEngine.Tests.csproj SB.FlowEngine.Tests/SmokeTest.cs sb-framework.sln
git -c commit.gpgsign=false commit -m "chore: SB.FlowEngine 项目脚手架(依赖SB.Core+xUnit测试)"
```

---

## Task 2: 契约类型

**Files:**
- Create: `SB.FlowEngine/Contracts/FlowGraph.cs`
- Create: `SB.FlowEngine/Contracts/ModuleDefinition.cs`
- Delete: `SB.FlowEngine/Placeholder.cs`(不再需要)

**Interfaces:**
- Produces: 全部契约类型 FlowGraph/FlowNode/FlowEdge/FlowVariable/NodePosition / ModuleDefinition/ModulePort/ModuleConfigField/ConfigOption/TypeDefinition

- [ ] **Step 1: 写 FlowGraph.cs**

```csharp
namespace SB.FlowEngine.Contracts;

public sealed class NodePosition {
    public double X { get; set; }
    public double Y { get; set; }
}

public sealed class FlowNode {
    public string Id { get; set; } = "";
    public string ModuleId { get; set; } = "";
    public NodePosition Position { get; set; } = new();
    public Dictionary<string, object?> Config { get; set; } = new();
}

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

- [ ] **Step 2: 写 ModuleDefinition.cs**

```csharp
namespace SB.FlowEngine.Contracts;

public sealed class TypeDefinition {
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public string Color { get; set; } = "";
}

public sealed class ModulePort {
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public string? Type { get; set; }      // 仅 data 端口有
    public bool Required { get; set; }
}

public sealed class ConfigOption {
    public string Label { get; set; } = "";
    public object? Value { get; set; }
}

public sealed class ModuleConfigField {
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public string Widget { get; set; } = ""; // text/number/select/switch
    public List<ConfigOption>? Options { get; set; }
}

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
```

- [ ] **Step 3: 删 Placeholder.cs**

```bash
rm SB.FlowEngine/Placeholder.cs
```

- [ ] **Step 4: 写契约冒烟测试**

`SB.FlowEngine.Tests/ContractTests.cs`:
```csharp
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
```

- [ ] **Step 5: 编译 + 测试**

Run:
```bash
cd /Users/tianlong/projects/saibo/framework/sb-framework
dotnet test SB.FlowEngine.Tests/SB.FlowEngine.Tests.csproj -nologo 2>&1 | tail -3
```
Expected: 全部 passed(含 SmokeTest + 2 个 ContractTests)

- [ ] **Step 6: 提交**

```bash
git add SB.FlowEngine/Contracts/FlowGraph.cs SB.FlowEngine/Contracts/ModuleDefinition.cs SB.FlowEngine.Tests/ContractTests.cs
git rm SB.FlowEngine/Placeholder.cs
git -c commit.gpgsign=false commit -m "feat: 契约类型(FlowGraph/ModuleDefinition,对齐前端contract.ts)"
```

---

## Task 3: TruthyHelper(JS truthy 语义对齐)

**Files:**
- Create: `SB.FlowEngine/Execution/TruthyHelper.cs`
- Create: `SB.FlowEngine.Tests/TruthyHelperTests.cs`

**Interfaces:**
- Produces: `static class TruthyHelper { static bool IsTruthy(object? value); }` — 按 JS truthy 语义判定

- [ ] **Step 1: 写失败测试**

`SB.FlowEngine.Tests/TruthyHelperTests.cs`:
```csharp
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
```

- [ ] **Step 2: 跑测试看失败**

Run: `cd /Users/tianlong/projects/saibo/framework/sb-framework && dotnet test SB.FlowEngine.Tests --filter TruthyHelperTests -nologo 2>&1 | tail -5`
Expected: 编译失败(TruthyHelper 未定义)

- [ ] **Step 3: 实现**

`SB.FlowEngine/Execution/TruthyHelper.cs`:
```csharp
namespace SB.FlowEngine.Execution;

/// <summary>按 JS truthy 语义判定一个值的真假,用于 if 节点条件判断,与前端执行器对齐。</summary>
public static class TruthyHelper {
    public static bool IsTruthy(object? value) {
        switch (value) {
            case null: return false;
            case bool b: return b;
            case string s: return s.Length > 0;
            case sbyte or byte or short or ushort or int or uint or long or ulong:
                return Convert.ToInt64(value) != 0;
            case float f: return f != 0f && !float.IsNaN(f);
            case double d: return d != 0d && !double.IsNaN(d);
            case decimal m: return m != 0m;
            default: return true;   // 其余对象(非 null)视为 true
        }
    }
}
```

- [ ] **Step 4: 跑测试看通过**

Run: `cd /Users/tianlong/projects/saibo/framework/sb-framework && dotnet test SB.FlowEngine.Tests --filter TruthyHelperTests -nologo 2>&1 | tail -3`
Expected: 全部 passed(11 个 InlineData)

- [ ] **Step 5: 提交**

```bash
git add SB.FlowEngine/Execution/TruthyHelper.cs SB.FlowEngine.Tests/TruthyHelperTests.cs
git -c commit.gpgsign=false commit -m "feat: TruthyHelper(JS truthy语义对齐前端,纯函数+单测)"
```

---

## Task 4: 执行支撑类型(Context/接口/选项/结果/异常)

**Files:**
- Create: `SB.FlowEngine/Execution/FlowNodeContext.cs`
- Create: `SB.FlowEngine/Execution/IFlowNodeExecutor.cs`
- Create: `SB.FlowEngine/Execution/FlowExecutionOptions.cs`
- Create: `SB.FlowEngine/Execution/FlowExecutionResult.cs`
- Create: `SB.FlowEngine/Execution/FlowExecutionException.cs`

**Interfaces:**
- Produces:
  - `FlowNodeContext { string ModuleId; IReadOnlyDictionary<string,object?> Config; IReadOnlyDictionary<string,object?> Inputs }`
  - `interface IFlowNodeExecutor { string ModuleId; Task<IReadOnlyDictionary<string,object?>> ExecuteAsync(FlowNodeContext ctx, CancellationToken ct = default); }`
  - `FlowExecutionOptions { int MaxSteps = 100000; Action<string,string>? OnNodeStatus }`
  - `FlowExecutionResult { IReadOnlyDictionary<string,object?> Variables; int Steps }`
  - `FlowExecutionException : Exception`

- [ ] **Step 1: 写全部支撑类型**

`SB.FlowEngine/Execution/FlowNodeContext.cs`:
```csharp
namespace SB.FlowEngine.Execution;

public sealed class FlowNodeContext {
    public string ModuleId { get; init; } = "";
    public IReadOnlyDictionary<string, object?> Config { get; init; }
        = new Dictionary<string, object?>();
    public IReadOnlyDictionary<string, object?> Inputs { get; init; }
        = new Dictionary<string, object?>();
}
```

`SB.FlowEngine/Execution/IFlowNodeExecutor.cs`:
```csharp
namespace SB.FlowEngine.Execution;

/// <summary>业务节点执行器。业务层为每个节点类型实现一个,引擎按 ModuleId 分发。</summary>
public interface IFlowNodeExecutor {
    string ModuleId { get; }
    Task<IReadOnlyDictionary<string, object?>> ExecuteAsync(
        FlowNodeContext ctx, CancellationToken ct = default);
}
```

`SB.FlowEngine/Execution/FlowExecutionOptions.cs`:
```csharp
namespace SB.FlowEngine.Execution;

public sealed class FlowExecutionOptions {
    public int MaxSteps { get; set; } = 100_000;
    /// <summary>节点状态回调: (nodeId, "running"|"success"|"failed")</summary>
    public Action<string, string>? OnNodeStatus { get; set; }
}
```

`SB.FlowEngine/Execution/FlowExecutionResult.cs`:
```csharp
namespace SB.FlowEngine.Execution;

public sealed class FlowExecutionResult {
    public IReadOnlyDictionary<string, object?> Variables { get; init; }
        = new Dictionary<string, object?>();
    public int Steps { get; init; }
}
```

`SB.FlowEngine/Execution/FlowExecutionException.cs`:
```csharp
namespace SB.FlowEngine.Execution;

public sealed class FlowExecutionException : Exception {
    public FlowExecutionException(string message) : base(message) { }
}
```

- [ ] **Step 2: 编译**

Run: `cd /Users/tianlong/projects/saibo/framework/sb-framework && dotnet build SB.FlowEngine/SB.FlowEngine.csproj -nologo 2>&1 | grep -E "个错误|已成功生成" | head`
Expected: 0 错误

- [ ] **Step 3: 提交**

```bash
git add SB.FlowEngine/Execution/FlowNodeContext.cs SB.FlowEngine/Execution/IFlowNodeExecutor.cs SB.FlowEngine/Execution/FlowExecutionOptions.cs SB.FlowEngine/Execution/FlowExecutionResult.cs SB.FlowEngine/Execution/FlowExecutionException.cs
git -c commit.gpgsign=false commit -m "feat: 执行支撑类型(Context/IFlowNodeExecutor/Options/Result/Exception)"
```

---

## Task 5: FlowExecutor 执行引擎(核心)

**Files:**
- Create: `SB.FlowEngine/Execution/FlowExecutor.cs`
- Create: `SB.FlowEngine.Tests/ControlNodeTests.cs`
- Create: `SB.FlowEngine.Tests/DataPullTests.cs`
- Create: `SB.FlowEngine.Tests/ExecutorEndToEndTests.cs`

**Interfaces:**
- Consumes: 契约类型(Task 2)、TruthyHelper(Task 3)、支撑类型(Task 4)
- Produces:
  - `class FlowExecutor { FlowExecutor(IEnumerable<IFlowNodeExecutor> executors); Task<FlowExecutionResult> RunAsync(FlowGraph graph, IReadOnlyList<ModuleDefinition> modules, FlowExecutionOptions? options = null, CancellationToken ct = default); }`
  - 重复 ModuleId 注册 → 抛 FlowExecutionException

> 关键: RunAsync 收 modules 列表(方案A,对称前端),用于查业务节点的 ExecOutputs[0]。

- [ ] **Step 1: 写测试——控制节点(先写全部失败测试)**

`SB.FlowEngine.Tests/ControlNodeTests.cs`:
```csharp
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
```

- [ ] **Step 2: 写测试——数据拉取**

`SB.FlowEngine.Tests/DataPullTests.cs`:
```csharp
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
```

- [ ] **Step 3: 写测试——端到端 + 错误 + 终止**

`SB.FlowEngine.Tests/ExecutorEndToEndTests.cs`:
```csharp
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
```

- [ ] **Step 4: 跑测试看失败**

Run: `cd /Users/tianlong/projects/saibo/framework/sb-framework && dotnet test SB.FlowEngine.Tests -nologo 2>&1 | tail -5`
Expected: 编译失败(FlowExecutor 未定义)

- [ ] **Step 5: 实现 FlowExecutor**

`SB.FlowEngine/Execution/FlowExecutor.cs`:
```csharp
using SB.FlowEngine.Contracts;

namespace SB.FlowEngine.Execution;

/// <summary>图执行引擎:执行指针沿 exec 线走 + 数据沿 data 线拉取求值(只读缓存)。
/// 与前端 @saibo/flow-engine 执行器语义对齐。</summary>
public sealed class FlowExecutor {
    private static readonly HashSet<string> ControlIds = new()
        { "if", "loop", "goto", "label", "varGet", "varSet" };

    private readonly Dictionary<string, IFlowNodeExecutor> _executors;

    public FlowExecutor(IEnumerable<IFlowNodeExecutor> executors) {
        _executors = new();
        foreach (var e in executors) {
            if (!_executors.TryAdd(e.ModuleId, e))
                throw new FlowExecutionException($"重复的节点执行器 ModuleId: {e.ModuleId}");
        }
    }

    public async Task<FlowExecutionResult> RunAsync(
        FlowGraph graph, IReadOnlyList<ModuleDefinition> modules,
        FlowExecutionOptions? options = null, CancellationToken ct = default) {
        options ??= new FlowExecutionOptions();
        var nodeMap = graph.Nodes.ToDictionary(n => n.Id);
        var moduleMap = modules.ToDictionary(m => m.Id);
        var variables = new Dictionary<string, object?>();
        foreach (var v in graph.Variables) variables[v.Name] = v.Initial;
        var nodeOutputs = new Dictionary<string, IReadOnlyDictionary<string, object?>>();
        var loopCounters = new Dictionary<string, int>();

        // 沿 data 线拉取某节点的输入
        IReadOnlyDictionary<string, object?> GatherInputs(string nodeId) {
            var inputs = new Dictionary<string, object?>();
            foreach (var e in graph.Edges) {
                if (e.Kind != "data" || e.Target != nodeId) continue;
                if (!nodeMap.TryGetValue(e.Source, out var up)) continue;
                if (up.ModuleId == "varGet") {
                    var varName = up.Config.TryGetValue("varName", out var vn) ? vn as string : null;
                    inputs[e.TargetPort] = varName != null && variables.TryGetValue(varName, out var val) ? val : null;
                } else if (!ControlIds.Contains(up.ModuleId)) {
                    if (!nodeOutputs.TryGetValue(e.Source, out var outs))
                        throw new FlowExecutionException(
                            $"节点 {e.Source} 的输出在被读取前尚未执行，请检查执行顺序");
                    inputs[e.TargetPort] = outs.TryGetValue(e.SourcePort, out var ov) ? ov : null;
                }
                // 其他控制节点:忽略(无数据输出)
            }
            return inputs;
        }

        // 沿 exec 线找下一个节点
        string? NextByExec(string nodeId, string portId) {
            foreach (var e in graph.Edges)
                if (e.Kind == "exec" && e.Source == nodeId && e.SourcePort == portId)
                    return e.Target;
            return null;
        }

        string? pc = string.IsNullOrEmpty(graph.Entry) ? null : graph.Entry;
        int steps = 0;
        while (pc != null) {
            if (++steps > options.MaxSteps)
                throw new FlowExecutionException(
                    $"执行步数超过上限 {options.MaxSteps}(可能存在死循环)");
            if (!nodeMap.TryGetValue(pc, out var node)) break;
            options.OnNodeStatus?.Invoke(pc, "running");
            try {
                switch (node.ModuleId) {
                    case "if": {
                        var inputs = GatherInputs(pc);
                        var cond = inputs.TryGetValue("cond", out var c) ? c : null;
                        var branch = TruthyHelper.IsTruthy(cond) ? "true" : "false";
                        options.OnNodeStatus?.Invoke(pc, "success");
                        pc = NextByExec(pc, branch);
                        break;
                    }
                    case "loop": {
                        var inputs = GatherInputs(pc);
                        var count = ToInt(inputs.TryGetValue("count", out var ci) ? ci
                            : node.Config.TryGetValue("count", out var cc) ? cc : 0);
                        var done = loopCounters.TryGetValue(pc, out var d) ? d : 0;
                        if (done < count) {
                            loopCounters[pc] = done + 1;
                            options.OnNodeStatus?.Invoke(pc, "success");
                            pc = NextByExec(pc, "body");
                        } else {
                            loopCounters.Remove(pc);
                            options.OnNodeStatus?.Invoke(pc, "success");
                            pc = NextByExec(pc, "done");
                        }
                        break;
                    }
                    case "goto":
                        options.OnNodeStatus?.Invoke(pc, "success");
                        pc = NextByExec(pc, "to");
                        break;
                    case "label":
                        options.OnNodeStatus?.Invoke(pc, "success");
                        pc = NextByExec(pc, "out");
                        break;
                    case "varSet": {
                        var inputs = GatherInputs(pc);
                        var varName = node.Config.TryGetValue("varName", out var vn) ? vn as string : null;
                        if (varName != null)
                            variables[varName] = inputs.TryGetValue("value", out var val) ? val : null;
                        options.OnNodeStatus?.Invoke(pc, "success");
                        pc = NextByExec(pc, "out");
                        break;
                    }
                    case "varGet":
                        // 纯数据节点,正常不会成为 pc(无 exec 入口);若意外到达,直接跳过
                        options.OnNodeStatus?.Invoke(pc, "success");
                        pc = null;
                        break;
                    default: {
                        // 业务节点
                        if (!_executors.TryGetValue(node.ModuleId, out var exec))
                            throw new FlowExecutionException($"未知模块: {node.ModuleId}");
                        var inputs = GatherInputs(pc);
                        var outs = await exec.ExecuteAsync(
                            new FlowNodeContext { ModuleId = node.ModuleId, Config = node.Config, Inputs = inputs },
                            ct) ?? new Dictionary<string, object?>();
                        nodeOutputs[pc] = outs;
                        options.OnNodeStatus?.Invoke(pc, "success");
                        var mod = moduleMap.TryGetValue(node.ModuleId, out var m) ? m : null;
                        var firstOut = mod?.ExecOutputs.FirstOrDefault()?.Id;
                        pc = firstOut != null ? NextByExec(pc, firstOut) : null;
                        break;
                    }
                }
            } catch {
                options.OnNodeStatus?.Invoke(pc ?? "", "failed");
                throw;
            }
        }
        return new FlowExecutionResult { Variables = variables, Steps = steps };
    }

    private static int ToInt(object? value) => value switch {
        null => 0,
        int i => i,
        long l => (int)l,
        double d => (int)d,
        float f => (int)f,
        decimal m => (int)m,
        string s => int.TryParse(s, out var r) ? r : 0,
        _ => Convert.ToInt32(value),
    };
}
```

- [ ] **Step 6: 跑测试看通过**

Run: `cd /Users/tianlong/projects/saibo/framework/sb-framework && dotnet test SB.FlowEngine.Tests -nologo 2>&1 | tail -5`
Expected: 全部 passed(控制节点4 + 数据拉取3 + 端到端4 + 之前的契约/truthy/smoke)
> 若 If_true_branch 的 catch 块里 pc 为 null 触发警告,按实际编译提示调整(pc 在 catch 时可能已被赋 null,用 `pc ?? ""` 已处理)。

- [ ] **Step 7: 提交**

```bash
git add SB.FlowEngine/Execution/FlowExecutor.cs SB.FlowEngine.Tests/ControlNodeTests.cs SB.FlowEngine.Tests/DataPullTests.cs SB.FlowEngine.Tests/ExecutorEndToEndTests.cs
git -c commit.gpgsign=false commit -m "feat: FlowExecutor 执行引擎(执行指针+数据拉取+控制节点+终止保护,对齐前端)"
```

---

## Task 6: README + 全量验证

**Files:**
- Create: `SB.FlowEngine/README.md`

**Interfaces:** 无新代码,文档 + 验证

- [ ] **Step 1: 写 README.md**

`SB.FlowEngine/README.md`(中文,含用法):
```markdown
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
```

- [ ] **Step 2: 全量编译 + 测试**

Run:
```bash
cd /Users/tianlong/projects/saibo/framework/sb-framework
dotnet build SB.FlowEngine/SB.FlowEngine.csproj -nologo 2>&1 | grep -E "个错误|已成功生成" | head
dotnet test SB.FlowEngine.Tests -nologo 2>&1 | tail -4
```
Expected: 库 0 错误;全部测试 passed

- [ ] **Step 3: 验证 pack 能产出 nupkg**

Run:
```bash
cd /Users/tianlong/projects/saibo/framework/sb-framework
dotnet pack SB.FlowEngine/SB.FlowEngine.csproj -c Release -o /tmp/flowengine-pack -nologo 2>&1 | tail -3
ls /tmp/flowengine-pack/*.nupkg
```
Expected: 产出 SB.FlowEngine.0.1.0.nupkg(ProjectReference 自动转 SB.Framework.Core 包依赖)

- [ ] **Step 4: 提交**

```bash
git add SB.FlowEngine/README.md
git -c commit.gpgsign=false commit -m "docs: SB.FlowEngine README + 全量验证通过"
```

---

## Task 7: 发布到 baget

**Files:** 无代码改动(纯发布)

**Interfaces:** 产出 SB.FlowEngine 0.1.0 到 baget Mirror

> baget 发布:参照 framework 现有发包脚本/约定(memory: baget 在 .68,key 在 nexus#10,License 变体需 SB_LICENSE_KEY_DIR;但 SB.FlowEngine 不涉及 License 变体,是普通包)。实现前先看 framework/sb-framework/scripts/ 下的发包脚本和 docs/AI_BaGet_Feed.md。

- [ ] **Step 1: 看 framework 发包方式**

Run:
```bash
cd /Users/tianlong/projects/saibo/framework/sb-framework
ls scripts/ | grep -iE "publish|pack|nuget|baget"
cat docs/AI_BaGet_Feed.md 2>/dev/null | head -40
```
确认发包命令、baget 地址、API key 来源(nexus#10)。

- [ ] **Step 2: 按 framework 约定打包发布**

依 framework 现有方式(可能是脚本,也可能是 `dotnet nuget push`):
```bash
# 示例(以实际脚本/约定为准):
dotnet pack SB.FlowEngine/SB.FlowEngine.csproj -c Release -o nupkgs -nologo
dotnet nuget push nupkgs/SB.FlowEngine.0.1.0.nupkg --source <baget地址> --api-key <key>
```
Expected: 发布成功

- [ ] **Step 3: 验证 baget 上可见**

Run: 查 baget feed 确认 SB.FlowEngine 0.1.0 存在(用 framework 文档里的查询方式)。

- [ ] **Step 4: 提交(如有 nupkgs 或脚本改动)**

```bash
git -c commit.gpgsign=false commit -m "chore: 发布 SB.FlowEngine 0.1.0 到 baget" || echo "无文件改动,跳过"
```

---

## 自检

- **Spec 覆盖**: 契约类型(Task2)/truthy(Task3)/支撑类型(Task4)/FlowExecutor含控制节点+数据拉取+终止(Task5)/README+验证(Task6)/发布(Task7)。✓
- **类型一致**: FlowExecutor 构造 `IEnumerable<IFlowNodeExecutor>`、RunAsync(graph, modules, options, ct) 返回 FlowExecutionResult;IFlowNodeExecutor.ModuleId + ExecuteAsync(ctx,ct);FlowNodeContext.ModuleId/Config/Inputs;CONTROL_IDS 6 个 — 全程一致。✓
- **对齐前端**: 端口id(if:true/false/cond, loop:body/done/count, varSet:value/out 等)、错误文案三条逐字、truthy、缓存只读不重跑、ExecOutputs[0]、modules 列表(方案A)— 都对齐。✓
- **占位扫描**: 无 TBD;每步有完整代码;Task7 发布步骤依赖 framework 现有约定(已标注"以实际脚本为准",因发包方式是仓库既定的,实现时读 scripts/ 和 AI_BaGet_Feed.md)。
- **风险**: Task5 是核心,测试覆盖 if真假/loop迭代/varSet/数据传递/只执行一次/上游未执行抛错/未知模块/超步/重复ModuleId/顺序;truthy 单独 Task3 覆盖 0/""/null。Task1 的 SB.Framework.Core 依赖还原若失败,查 NuGet.config 指向 baget。

## 执行说明(给清空上下文后继续的你)

- 本计划自包含,从 Task 1 顺序执行。前置背景: spec 在 `docs/superpowers/specs/2026-06-22-sb-flowengine-design.md`,前端对照源见本文件顶部"前端对照源"。
- 推荐用 superpowers:subagent-driven-development 执行(每任务派 subagent + 审查),或 superpowers:executing-plans 直接执行。
- 项目在 `framework/sb-framework/`(已有 git 仓库,main 分支,用户习惯直接 main);提交用中文 Conventional Commit + `git -c commit.gpgsign=false`;**不要 push**,用户统一处理。
- Task 5(FlowExecutor)是核心,务必对照前端 executor.ts 确认语义一致,且测试全绿再继续。
- Task 7 发布是对外动作,执行前与用户确认。

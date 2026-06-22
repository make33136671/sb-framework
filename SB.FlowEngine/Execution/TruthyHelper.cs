using System.Text.Json;

namespace SB.FlowEngine.Execution;

/// <summary>按 JS truthy 语义判定一个值的真假,用于 if 节点条件判断,与前端执行器对齐。</summary>
public static class TruthyHelper {
    public static bool IsTruthy(object? value) {
        // FlowGraph 经 JSON 反序列化时,object? 值实际是 JsonElement,先解包再判定。
        if (value is JsonElement je) return IsTruthyJson(je);
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

    private static bool IsTruthyJson(JsonElement je) => je.ValueKind switch {
        JsonValueKind.Null or JsonValueKind.Undefined => false,
        JsonValueKind.False => false,
        JsonValueKind.True => true,
        JsonValueKind.Number => je.TryGetDouble(out var d) && d != 0d && !double.IsNaN(d),
        JsonValueKind.String => je.GetString()!.Length > 0,
        _ => true,   // Array/Object 非空对象视为 true(对齐 JS:[]/{} 也 truthy)
    };
}

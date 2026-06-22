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

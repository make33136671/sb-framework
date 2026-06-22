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

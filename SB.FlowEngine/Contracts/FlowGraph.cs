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

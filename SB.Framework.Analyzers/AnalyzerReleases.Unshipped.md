; Please read https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md
; Unshipped rules (rules not yet in a released package).

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
SB0001 | Usage | Error | 禁止使用 DateTime.Now/Today/UtcNow，统一用 AppTime
SB0002 | Usage | Error | 禁止在未开白名单的 action/controller 使用 [FromBody]

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SB.Framework.Analyzers
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class BanDateTimeNowAnalyzer : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "SB0001";

        private static readonly LocalizableString Title = "禁止使用 DateTime.Now/Today/UtcNow";
        private static readonly LocalizableString MessageFormat = "请使用 AppTime.{0} 替代 DateTime.{0}";
        private static readonly LocalizableString Description = "项目统一使用 AppTime 工具类处理时间，禁止直接使用 DateTime.Now/Today/UtcNow.";
        private const string Category = "Usage";

        private static readonly DiagnosticDescriptor Rule = new(
            DiagnosticId,
            Title,
            MessageFormat,
            Category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: Description);

        private static readonly ImmutableHashSet<string> BannedMembers =
            ImmutableHashSet.Create("Now", "Today", "UtcNow");

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
            => ImmutableArray.Create(Rule);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSyntaxNodeAction(AnalyzeMemberAccess, SyntaxKind.SimpleMemberAccessExpression);
        }

        private static void AnalyzeMemberAccess(SyntaxNodeAnalysisContext context)
        {
            var memberAccess = (MemberAccessExpressionSyntax)context.Node;
            var memberName = memberAccess.Name.Identifier.Text;

            if (!BannedMembers.Contains(memberName))
                return;

            // 只靠语义判断（符号的声明类型是否 System.DateTime + 是否属性），
            // 不限制左侧语法写法 —— 这样 DateTime.Now / System.DateTime.Now /
            // global::System.DateTime.Now 全部覆盖，且语义保证一定是 System.DateTime 不误伤。
            // （旧版只匹配 IdentifierName "DateTime"，漏掉带命名空间前缀的写法。）
            var symbolInfo = context.SemanticModel.GetSymbolInfo(memberAccess);
            if (symbolInfo.Symbol is IPropertySymbol propSymbol
                && propSymbol.ContainingType?.ToString() == "System.DateTime")
            {
                var diagnostic = Diagnostic.Create(Rule, memberAccess.GetLocation(), memberName);
                context.ReportDiagnostic(diagnostic);
            }
        }
    }
}

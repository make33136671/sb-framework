using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SB.Framework.Analyzers
{
    /// <summary>
    /// 禁止在 FreeSql 的 [Column] 特性上写 DbType = "..." 硬编码数据库类型。
    ///
    /// 动机:saibo 框架要求同一套实体同时兼容 MySQL 与 PostgreSQL(派生项目立项时二选一)。
    /// DbType 字符串会把实体锁死在单一方言 —— 典型如 DbType = "timestamp":
    ///   - PG 下是 timestamp without time zone(合理);
    ///   - MySQL 下 timestamp 范围仅 1970-2038 且隐式时区转换(隐患)。
    /// 不写 DbType 时,FreeSql CodeFirst 会按当前 DataType 自动映射:
    /// DateTime → MySQL datetime(6) / PG timestamp(6);长文本用 StringLength = -2 → text。
    /// 极少数确需方言的场景,集中放在 WebApi 的 FreeSqlSetup(fsql.CodeFirst.ConfigEntity)按
    /// DataType 分支配置,禁止散落在实体上。确要硬写可在项目文件里 NoWarn:SB0003。
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class BanColumnDbTypeAnalyzer : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "SB0003";

        private static readonly LocalizableString Title = "禁止在 [Column] 特性上硬编码 DbType";
        private static readonly LocalizableString MessageFormat =
            "'{0}' 上硬编码了 DbType = \"{1}\"。同一套实体必须同时兼容 MySQL/PostgreSQL:删除 DbType,由 FreeSql 按 DataType 自动映射(时间用 DateTime、长文本用 StringLength = -2);确需方言差异,集中配置到 FreeSqlSetup 的 fsql.CodeFirst.ConfigEntity 按 DataType 分支处理";
        private static readonly LocalizableString Description =
            "实体方言硬编码DbType会把模型锁死在单一数据库,导致 MySQL/PG 双兼容需要维护两套实体. FreeSql CodeFirst 不写 DbType 时按当前 DataType 自动映射类型,一套实体两库通吃. 特殊需求集中在 FreeSqlSetup 按 DataType 分支配置.";
        private const string Category = "Usage";

        private static readonly DiagnosticDescriptor Rule = new(
            DiagnosticId,
            Title,
            MessageFormat,
            Category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: Description);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
            => ImmutableArray.Create(Rule);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            // 监听 attribute 节点:只有 [Column(...)] 上才可能写 DbType = "..."
            context.RegisterSyntaxNodeAction(AnalyzeAttribute, SyntaxKind.Attribute);
        }

        private static void AnalyzeAttribute(SyntaxNodeAnalysisContext context)
        {
            var attribute = (AttributeSyntax)context.Node;

            // 纯语法匹配特性名 Column(不依赖语义引用,分析器不引用 framework 程序集)
            if (GetSimpleName(attribute.Name) != "Column")
                return;

            if (attribute.ArgumentList == null)
                return;

            foreach (var argument in attribute.ArgumentList.Arguments)
            {
                // 只拦命名参数 DbType = "字面量字符串"
                if (argument.NameEquals == null)
                    continue;
                if (argument.NameEquals.Name == null)
                    continue;
                if (GetSimpleName(argument.NameEquals.Name) != "DbType")
                    continue;

                var literal = argument.Expression as LiteralExpressionSyntax;
                if (literal == null || !literal.IsKind(SyntaxKind.StringLiteralExpression))
                    continue;

                var diagnostic = Diagnostic.Create(
                    Rule,
                    argument.GetLocation(),
                    GetOwnerDisplayName(attribute),
                    literal.Token.ValueText);
                context.ReportDiagnostic(diagnostic);
            }
        }

        /// <summary>报错定位取 [Column] 所在成员/类型的可读名,找不到就用 "Column"。</summary>
        private static string GetOwnerDisplayName(AttributeSyntax attribute)
        {
            var member = attribute.FirstAncestorOrSelf<MemberDeclarationSyntax>();
            if (member is PropertyDeclarationSyntax prop) return prop.Identifier.Text;
            if (member is FieldDeclarationSyntax field && field.Declaration.Variables.Count > 0)
                return field.Declaration.Variables[0].Identifier.Text;
            var type = attribute.FirstAncestorOrSelf<TypeDeclarationSyntax>();
            return type?.Identifier.Text ?? "Column";
        }

        /// <summary>取 attribute/参数名的最后一段(去掉命名空间限定)。</summary>
        private static string GetSimpleName(NameSyntax nameSyntax)
        {
            switch (nameSyntax)
            {
                case IdentifierNameSyntax id:
                    return id.Identifier.Text;
                case QualifiedNameSyntax qualified:
                    return qualified.Right.Identifier.Text;
                case AliasQualifiedNameSyntax alias:
                    return alias.Name.Identifier.Text;
                default:
                    return nameSyntax.ToString();
            }
        }
    }
}

using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SB.Framework.Analyzers
{
    /// <summary>
    /// 禁止在 controller action 参数上使用 [FromBody]，除非该 action 或其所在类
    /// 标了 [AllowJsonBody] 白名单。
    ///
    /// 动机：saibo 系 admin 接口约定走 [FromForm]，前端统一 api.postForm。
    /// 历史上有人把后端改成 [FromBody]（或反之）却没同步前端，导致 Content-Type 不一致、
    /// 参数接成 null（典型：登录报"密码错误"）。把这种隐患从"运行时静默出错"提前到"编译期报错"。
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class BanFromBodyAnalyzer : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "SB0002";

        private static readonly LocalizableString Title = "禁止使用 [FromBody]（未开 JSON 白名单）";
        private static readonly LocalizableString MessageFormat =
            "参数 '{0}' 使用了 [FromBody]，但所在 action/controller 未标 [AllowJsonBody]。saibo admin 接口默认走 [FromForm]（前端 api.postForm）；确需 JSON 体请在该 action 或 controller 上加 [AllowJsonBody] 显式开白名单，并确保前端用 api.post（JSON）";
        private static readonly LocalizableString Description =
            "saibo 系后端接口统一约定 [FromForm]，避免前后端 Content-Type 不一致导致参数为 null。复杂嵌套对象确需 JSON 的，用 [AllowJsonBody] 显式开白名单。";
        private const string Category = "Usage";

        private static readonly DiagnosticDescriptor Rule = new(
            DiagnosticId,
            Title,
            MessageFormat,
            Category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: Description);

        // 同时匹配带/不带 Attribute 后缀的两种写法
        private const string FromBodyName = "FromBody";
        private const string FromBodyNameFull = "FromBodyAttribute";
        private const string AllowJsonBodyName = "AllowJsonBody";
        private const string AllowJsonBodyNameFull = "AllowJsonBodyAttribute";

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
            => ImmutableArray.Create(Rule);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            // 监听参数节点：参数上才可能挂 [FromBody]
            context.RegisterSyntaxNodeAction(AnalyzeParameter, SyntaxKind.Parameter);
        }

        private static void AnalyzeParameter(SyntaxNodeAnalysisContext context)
        {
            var parameter = (ParameterSyntax)context.Node;

            // 1. 找参数上的 [FromBody]
            var fromBodyAttr = FindAttribute(parameter.AttributeLists, FromBodyName, FromBodyNameFull);
            if (fromBodyAttr == null)
                return;

            // 2. 找所属 method（往上找最近的方法声明）
            var method = parameter.FirstAncestorOrSelf<MethodDeclarationSyntax>();
            if (method == null)
                return;

            // 3. method 上有 [AllowJsonBody] → 放行
            if (FindAttribute(method.AttributeLists, AllowJsonBodyName, AllowJsonBodyNameFull) != null)
                return;

            // 4. 所属 class 上有 [AllowJsonBody] → 放行
            var typeDecl = method.FirstAncestorOrSelf<TypeDeclarationSyntax>();
            if (typeDecl != null
                && FindAttribute(typeDecl.AttributeLists, AllowJsonBodyName, AllowJsonBodyNameFull) != null)
                return;

            // 5. 都没有 → 报错（定位到 [FromBody] 这个 attribute 本身）
            var diagnostic = Diagnostic.Create(
                Rule,
                fromBodyAttr.GetLocation(),
                parameter.Identifier.Text);
            context.ReportDiagnostic(diagnostic);
        }

        /// <summary>
        /// 在一组 AttributeList 里按名字找 attribute（纯语法匹配，不依赖语义/引用，
        /// 因为分析器是 netstandard2.0、不引用 framework 程序集）。
        /// 同时容忍 "FromBody" / "FromBodyAttribute" / "Mvc.FromBody" 这些写法。
        /// </summary>
        private static AttributeSyntax? FindAttribute(
            SyntaxList<AttributeListSyntax> attributeLists,
            string shortName,
            string fullName)
        {
            foreach (var list in attributeLists)
            {
                foreach (var attr in list.Attributes)
                {
                    var name = GetSimpleName(attr.Name);
                    if (name == shortName || name == fullName)
                        return attr;
                }
            }
            return null;
        }

        /// <summary>取 attribute 名字的最后一段（去掉命名空间限定）。</summary>
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

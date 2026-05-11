using System.Collections.Immutable;
using System.Composition;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SB.Framework.Analyzers
{
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(BanDateTimeNowCodeFix)), Shared]
    public class BanDateTimeNowCodeFix : CodeFixProvider
    {
        public override ImmutableArray<string> FixableDiagnosticIds
            => ImmutableArray.Create(BanDateTimeNowAnalyzer.DiagnosticId);

        public override FixAllProvider GetFixAllProvider()
            => WellKnownFixAllProviders.BatchFixer;

        public override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken);
            if (root == null) return;

            foreach (var diagnostic in context.Diagnostics)
            {
                var node = root.FindNode(diagnostic.Location.SourceSpan);
                if (node is not MemberAccessExpressionSyntax memberAccess) continue;

                var memberName = memberAccess.Name.Identifier.Text;

                context.RegisterCodeFix(
                    CodeAction.Create(
                        title: $"替换为 AppTime.{memberName}",
                        createChangedDocument: _ =>
                        {
                            var newExpression = SyntaxFactory.MemberAccessExpression(
                                SyntaxKind.SimpleMemberAccessExpression,
                                SyntaxFactory.IdentifierName("AppTime"),
                                SyntaxFactory.IdentifierName(memberName))
                                .WithTriviaFrom(memberAccess);

                            var newRoot = root.ReplaceNode(memberAccess, newExpression);
                            return Task.FromResult(context.Document.WithSyntaxRoot(newRoot));
                        },
                        equivalenceKey: $"UseAppTime_{memberName}"),
                    diagnostic);
            }
        }
    }
}

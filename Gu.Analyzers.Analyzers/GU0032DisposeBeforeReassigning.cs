﻿namespace Gu.Analyzers
{
    using System.Collections.Immutable;
    using System.Threading;

    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.CSharp.Syntax;
    using Microsoft.CodeAnalysis.Diagnostics;

    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    internal class GU0032DisposeBeforeReassigning : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "GU0032";
        private const string Title = "Dispose before re-assigning.";
        private const string MessageFormat = "Dispose before re-assigning.";
        private const string Description = "Dispose before re-assigning.";
        private static readonly string HelpLink = Analyzers.HelpLink.ForId(DiagnosticId);

        private static readonly DiagnosticDescriptor Descriptor = new DiagnosticDescriptor(
            id: DiagnosticId,
            title: Title,
            messageFormat: MessageFormat,
            category: AnalyzerCategory.Correctness,
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: AnalyzerConstants.EnabledByDefault,
            description: Description,
            helpLinkUri: HelpLink);

        /// <inheritdoc/>
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
            ImmutableArray.Create(Descriptor);

        /// <inheritdoc/>
        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSyntaxNodeAction(HandleAssignment, SyntaxKind.SimpleAssignmentExpression);
            context.RegisterSyntaxNodeAction(HandleInvocation, SyntaxKind.InvocationExpression);
        }

        private static void HandleAssignment(SyntaxNodeAnalysisContext context)
        {
            if (context.IsExcludedFromAnalysis())
            {
                return;
            }

            var assignment = (AssignmentExpressionSyntax)context.Node;
            if (!Disposable.IsPotentialCreation(assignment.Right, context.SemanticModel, context.CancellationToken))
            {
                return;
            }

            var left = context.SemanticModel.GetSymbolSafe(assignment.Left, context.CancellationToken);
            if (left == KnownSymbol.SerialDisposable.Disposable)
            {
                return;
            }

            if (left is ILocalSymbol || left is IParameterSymbol)
            {
                if (!IsVariableAssignedBefore(left, assignment, context.SemanticModel, context.CancellationToken) ||
                    IsDisposedBefore(left, assignment, context.SemanticModel, context.CancellationToken))
                {
                    return;
                }

                context.ReportDiagnostic(Diagnostic.Create(Descriptor, assignment.GetLocation()));
                return;
            }

            if (assignment.FirstAncestorOrSelf<MethodDeclarationSyntax>() != null)
            {
                if (IsDisposedBefore(left, assignment, context.SemanticModel, context.CancellationToken))
                {
                    return;
                }

                context.ReportDiagnostic(Diagnostic.Create(Descriptor, assignment.GetLocation()));
            }
        }

        private static void HandleInvocation(SyntaxNodeAnalysisContext context)
        {
            if (context.IsExcludedFromAnalysis())
            {
                return;
            }

            var invocation = (InvocationExpressionSyntax)context.Node;
            if (invocation.ArgumentList == null ||
                invocation.ArgumentList.Arguments.Count == 0)
            {
                return;
            }

            foreach (var argument in invocation.ArgumentList.Arguments)
            {
                if (!argument.RefOrOutKeyword.IsKind(SyntaxKind.OutKeyword))
                {
                    continue;
                }

                if (!Disposable.IsPotentialCreation(argument.Expression, context.SemanticModel, context.CancellationToken))
                {
                    return;
                }

                var argSymbol = context.SemanticModel.GetSymbolSafe(argument.Expression, context.CancellationToken);
                if (argSymbol == KnownSymbol.SerialDisposable.Disposable)
                {
                    return;
                }

                if (argSymbol is ILocalSymbol || argSymbol is IParameterSymbol)
                {
                    if (!IsVariableAssignedBefore(argSymbol, argument.Expression, context.SemanticModel, context.CancellationToken) ||
                        IsDisposedBefore(argSymbol, invocation, context.SemanticModel, context.CancellationToken))
                    {
                        return;
                    }

                    context.ReportDiagnostic(Diagnostic.Create(Descriptor, argument.GetLocation()));
                    return;
                }

                if (invocation.FirstAncestorOrSelf<MethodDeclarationSyntax>() != null)
                {
                    if (IsDisposedBefore(argSymbol, invocation, context.SemanticModel, context.CancellationToken))
                    {
                        return;
                    }

                    context.ReportDiagnostic(Diagnostic.Create(Descriptor, argument.GetLocation()));
                }
            }
        }

        private static bool IsVariableAssignedBefore(ISymbol symbol, ExpressionSyntax assignment, SemanticModel semanticModel, CancellationToken cancellationToken)
        {
            var parameter = symbol as IParameterSymbol;
            if (parameter?.RefKind == RefKind.Ref)
            {
                return true;
            }

            VariableDeclaratorSyntax declarator;
            if (symbol.TryGetSingleDeclaration(cancellationToken, out declarator))
            {
                if (Disposable.IsPotentialCreation(declarator.Initializer?.Value, semanticModel, cancellationToken))
                {
                    return true;
                }
            }

            using (var pooled = AssignmentWalker.Create(assignment.FirstAncestorOrSelf<MemberDeclarationSyntax>()))
            {
                foreach (var previousAssignment in pooled.Item.Assignments)
                {
                    if (previousAssignment.SpanStart >= assignment.SpanStart)
                    {
                        continue;
                    }

                    if (symbol.Equals(semanticModel.GetSymbolSafe(previousAssignment.Left, cancellationToken)))
                    {
                        if (Disposable.IsPotentialCreation(previousAssignment.Right, semanticModel, cancellationToken))
                        {
                            return true;
                        }
                    }
                }
            }

            var statement = assignment.FirstAncestorOrSelf<StatementSyntax>();
            if (statement == null)
            {
                return false;
            }

            using (var pooled = InvocationWalker.Create(assignment.FirstAncestorOrSelf<MemberDeclarationSyntax>()))
            {
                foreach (var previousInvocation in pooled.Item.Invocations)
                {
                    if (previousInvocation.SpanStart >= statement.SpanStart ||
                        previousInvocation.ArgumentList == null ||
                        previousInvocation.ArgumentList.Arguments.Count == 0)
                    {
                        continue;
                    }

                    foreach (var argument in previousInvocation.ArgumentList.Arguments)
                    {
                        if (!argument.RefOrOutKeyword.IsKind(SyntaxKind.OutKeyword))
                        {
                            continue;
                        }

                        if (symbol.Equals(semanticModel.GetSymbolSafe(argument.Expression, cancellationToken)))
                        {
                            if (Disposable.IsPotentialCreation(argument.Expression, semanticModel, cancellationToken))
                            {
                                return true;
                            }
                        }
                    }
                }
            }

            return false;
        }

        private static bool IsDisposedBefore(ISymbol symbol, ExpressionSyntax assignment, SemanticModel semanticModel, CancellationToken cancellationToken)
        {
            using (var pooled = InvocationWalker.Create(assignment.FirstAncestorOrSelf<MemberDeclarationSyntax>()))
            {
                foreach (var invocation in pooled.Item.Invocations)
                {
                    if (invocation.SpanStart > assignment.SpanStart)
                    {
                        break;
                    }

                    var invokedSymbol = semanticModel.GetSymbolSafe(invocation, cancellationToken);
                    if (invokedSymbol?.Name != "Dispose")
                    {
                        continue;
                    }

                    var statement = invocation.FirstAncestorOrSelf<StatementSyntax>();
                    if (statement != null)
                    {
                        using (var pooledStatement = IdentifierNameWalker.Create(statement))
                        {
                            foreach (var identifierName in pooledStatement.Item.IdentifierNames)
                            {
                                if (symbol.Equals(semanticModel.GetSymbolSafe(identifierName, cancellationToken)))
                                {
                                    return true;
                                }
                            }
                        }
                    }
                }
            }

            return false;
        }
    }
}
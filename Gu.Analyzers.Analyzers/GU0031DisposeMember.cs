﻿namespace Gu.Analyzers
{
    using System.Collections.Immutable;

    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.CSharp.Syntax;
    using Microsoft.CodeAnalysis.Diagnostics;

    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    internal class GU0031DisposeMember : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "GU0031";
        private const string Title = "Dispose member.";
        private const string MessageFormat = "Dispose member.";
        private const string Description = "Dispose the member as it is assigned with a created `IDisposable`s within the type.";
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
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Descriptor);

        /// <inheritdoc/>
        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSyntaxNodeAction(HandleField, SyntaxKind.FieldDeclaration);
            context.RegisterSyntaxNodeAction(HandleProperty, SyntaxKind.PropertyDeclaration);
        }

        private static void HandleField(SyntaxNodeAnalysisContext context)
        {
            var field = (IFieldSymbol)context.ContainingSymbol;
            if (field.IsStatic)
            {
                return;
            }

            if (Disposable.IsPotentiallyAssignedWithCreatedDisposable(field, context.SemanticModel, context.CancellationToken))
            {
                CheckThatMemberIsDisposed(context);
            }
        }

        private static void HandleProperty(SyntaxNodeAnalysisContext context)
        {
            var property = (IPropertySymbol)context.ContainingSymbol;
            if (property.IsStatic ||
                property.IsIndexer)
            {
                return;
            }

            var propertyDeclaration = (PropertyDeclarationSyntax)context.Node;
            if (propertyDeclaration.ExpressionBody != null)
            {
                return;
            }

            AccessorDeclarationSyntax setter;
            if (propertyDeclaration.TryGetSetAccessorDeclaration(out setter) &&
                setter.Body != null)
            {
                // Handle the backing field
                return;
            }

            if (Disposable.IsPotentiallyAssignedWithCreatedDisposable(property, context.SemanticModel, context.CancellationToken))
            {
                CheckThatMemberIsDisposed(context);
            }
        }

        private static void CheckThatMemberIsDisposed(SyntaxNodeAnalysisContext context)
        {
            var containingType = context.ContainingSymbol.ContainingType;

            IMethodSymbol disposeMethod;
            if (!Disposable.IsAssignableTo(containingType) || !Disposable.TryGetDisposeMethod(containingType, out disposeMethod))
            {
                return;
            }

            foreach (var declaration in disposeMethod.Declarations(context.CancellationToken))
            {
                using (var pooled = IdentifierNameWalker.Create(declaration))
                {
                    foreach (var identifier in pooled.Item.IdentifierNames)
                    {
                        if (identifier.Identifier.ValueText != context.ContainingSymbol.Name)
                        {
                            continue;
                        }

                        var symbol = context.SemanticModel.GetSymbolSafe(identifier, context.CancellationToken);
                        if (ReferenceEquals(symbol, context.ContainingSymbol))
                        {
                            return;
                        }
                    }
                }
            }

            context.ReportDiagnostic(Diagnostic.Create(Descriptor, context.Node.GetLocation()));
        }
    }
}
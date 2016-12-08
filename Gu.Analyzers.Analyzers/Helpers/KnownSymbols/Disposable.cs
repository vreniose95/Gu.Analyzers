namespace Gu.Analyzers
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Threading;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.CSharp.Syntax;

    internal static class Disposable
    {
        internal static bool IsCreation(ExpressionSyntax expression, SemanticModel semanticModel, CancellationToken cancellationToken)
        {
            if (expression is ObjectCreationExpressionSyntax)
            {
                return true;
            }

            var symbol = semanticModel.SemanticModelFor(expression)
                                      .GetSymbolInfo(expression, cancellationToken)
                                      .Symbol;
            if (symbol is IFieldSymbol)
            {
                return false;
            }

            if (symbol is IMethodSymbol)
            {
                MethodDeclarationSyntax methodDeclaration;
                if (symbol.TryGetDeclaration(cancellationToken, out methodDeclaration))
                {
                    ExpressionSyntax returnValue;
                    if (methodDeclaration.TryGetReturnExpression(out returnValue))
                    {
                        return IsCreation(returnValue, semanticModel, cancellationToken);
                    }
                }

                return true;
            }

            var property = symbol as IPropertySymbol;
            if (property != null)
            {
                if (property == KnownSymbol.PasswordBox.SecurePassword)
                {
                    return true;
                }

                PropertyDeclarationSyntax propertyDeclaration;
                if (property.TryGetDeclaration(cancellationToken, out propertyDeclaration))
                {
                    if (propertyDeclaration.ExpressionBody != null)
                    {
                        return IsCreation(propertyDeclaration.ExpressionBody.Expression, semanticModel, cancellationToken);
                    }

                    AccessorDeclarationSyntax getter;
                    if (propertyDeclaration.TryGetGetAccessorDeclaration(out getter))
                    {
                        ExpressionSyntax returnValue;
                        if (getter.Body.TryGetReturnExpression(out returnValue))
                        {
                            return IsCreation(returnValue, semanticModel, cancellationToken);
                        }
                    }
                }

                return false;
            }

            var local = symbol as ILocalSymbol;
            if (local != null)
            {
                VariableDeclaratorSyntax variable;
                if (local.TryGetDeclaration(cancellationToken, out variable))
                {
                    return IsCreation(variable.Initializer.Value, semanticModel, cancellationToken);
                }
            }

            return false;
        }

        internal static bool IsAssignableTo(ITypeSymbol type)
        {
            if (type == null)
            {
                return false;
            }

            ITypeSymbol _;
            return type == KnownSymbol.IDisposable ||
                   type.AllInterfaces.TryGetSingle(x => x == KnownSymbol.IDisposable, out _);
        }

        internal sealed class DisposeWalker : CSharpSyntaxWalker, IDisposable
        {
            private static readonly ConcurrentQueue<DisposeWalker> Cache = new ConcurrentQueue<DisposeWalker>();
            private readonly List<MemberAccessExpressionSyntax> disposeCalls = new List<MemberAccessExpressionSyntax>();

            private SemanticModel semanticModel;
            private CancellationToken cancellationToken;

            private DisposeWalker()
            {
            }

            public IReadOnlyList<MemberAccessExpressionSyntax> DisposeCalls => this.disposeCalls;

            public static DisposeWalker Create(BlockSyntax block, SemanticModel semanticModel, CancellationToken cancellationToken)
            {
                DisposeWalker walker;
                if (!Cache.TryDequeue(out walker))
                {
                    walker = new DisposeWalker();
                }

                walker.disposeCalls.Clear();
                walker.semanticModel = semanticModel;
                walker.cancellationToken = cancellationToken;
                walker.Visit(block);
                return walker;
            }

            public override void VisitInvocationExpression(InvocationExpressionSyntax node)
            {
                var symbol = this.semanticModel.SemanticModelFor(node).GetSymbolInfo(node, this.cancellationToken).Symbol;
                if (symbol.Name == KnownSymbol.IDisposable.Dispose.Name)
                {
                    this.disposeCalls.Add((MemberAccessExpressionSyntax)node.Expression);
                }

                base.VisitInvocationExpression(node);
            }

            public void Dispose()
            {
                this.disposeCalls.Clear();
                this.semanticModel = null;
                this.cancellationToken = CancellationToken.None;
                Cache.Enqueue(this);
            }
        }
    }
}
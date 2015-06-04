// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Linq.Expressions;
using JetBrains.Annotations;
using Microsoft.Data.Entity.Relational.Query.Sql;
using Microsoft.Data.Entity.Utilities;

namespace Microsoft.Data.Entity.Relational.Query.Expressions
{
    public class AliasExpression : ExtensionExpression
    {
        private readonly Expression _expression;

        public AliasExpression([NotNull] Expression expression)
            : base(Check.NotNull(expression, nameof(expression)).Type)
        {
            _expression = expression;
        }

        public AliasExpression([CanBeNull] string alias, [NotNull] Expression expression)
            : base(Check.NotNull(expression, nameof(expression)).Type)
        {
            Alias = alias;
            _expression = expression;
        }

        public virtual string Alias { get; [param: NotNull] set; }

        public virtual Expression Expression => _expression;

        public virtual bool Projected { get; set; } = false;

        protected override Expression Accept([NotNull] ExpressionVisitor visitor)
        {
            Check.NotNull(visitor, nameof(visitor));

            var specificVisitor = visitor as ISqlExpressionVisitor;

            return specificVisitor != null
                ? specificVisitor.VisitAliasExpression(this)
                : base.Accept(visitor);
        }

        protected override Expression VisitChildren(ExpressionVisitor visitor)
        {
            var newInnerExpression = visitor.Visit(_expression);

            return newInnerExpression != _expression ? new AliasExpression(Alias, newInnerExpression) : this;
        }

        public override string ToString() 
            => this.TryGetColumnExpression()?.ToString() ?? Expression.NodeType + " " + Alias;
    }
}

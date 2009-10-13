using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Laan.SQL.Parser.Expressions;
using Laan.SQL.Parser;

namespace Laan.SQL.Formatter
{
    internal class ExpressionFormatter : IIndentable
    {
        private const int MaxColumnWidth = 80;
        private int _indentLevel;
        private string _indent;

        public ExpressionFormatter( string indent, int indentLevel )
        {
            _indentLevel = indentLevel;
            _indent = indent;
        }

        private int GetCurrentColumn( StringBuilder sql )
        {
            return sql.ToString().Split( '\n' ).Last().Length;
        }
        
        internal string GetIndent( string indent, int indentLevel, bool includeNewLine )
        {
            string newLine = includeNewLine ? "\r\n" : "";
            StringBuilder result = new StringBuilder( newLine );
            for ( int index = 0; index < indentLevel; index++ )
                result.Append( indent );
            return result.ToString();
        }

        internal string GetIndent( string indent, int indentLevel )
        {
            return GetIndent( indent, indentLevel, true );
        }

        private string FormatCaseElseExpression( int offset, CaseExpression caseSwitch, int indentLevel )
        {
            return String.Format(
                "{0}ELSE{1}{2}",
                GetIndent( _indent, indentLevel ),
                GetIndent( _indent, indentLevel + 1 ),
                caseSwitch.Else.FormattedValue( offset, _indent, _indentLevel )
            );
        }

        private bool CanInlineExpression( Expression expr, int offset )
        {
            //int startingColumn = offset +_indentLevel * _indent.Length;
            return
                expr is IInlineFormattable &&
                ( (IInlineFormattable) expr ).CanInline &&
                expr.Value.Length < MaxColumnWidth - offset;
        }

        internal string GetBooleanExpression( CriteriaExpression expr, int offset )
        {
            // this code ensures the boolean expression is indented once
            // ie.
            // (
            // 
            //     A.ID IS NULL
            //     OR
            //     A.ID = 10
            //
            // )
            if ( expr.Parent is NestedExpression )
            {
                _indentLevel++;
                return String.Format(
                    "{0}{1}{2}{1}{3}",
                    expr.Left.FormattedValue( offset, _indent, _indentLevel ),
                    GetIndent( _indent, _indentLevel, true ),
                    expr.Operator,
                    expr.Right.FormattedValue( offset, _indent, _indentLevel )
                );
            }

            // this code ensures that chained boolean expressions are continued at the current nest level
            // ie.
            // (
            // 
            //     A.ID IS NULL
            //     OR
            //     A.ID = 10
            //     OR
            //     A.ID = 20
            //
            // )
            if ( expr.Parent is CriteriaExpression && expr.HasAncestorOfType( typeof( NestedExpression ) ) )
            {
                return String.Format(
                    "{0}{1}{2}{3}{2}{4}",
                    GetIndent( _indent, _indentLevel - 1, false ),
                    expr.Left.FormattedValue( offset, _indent, _indentLevel ),
                    GetIndent( _indent, _indentLevel, true ),
                    expr.Operator,
                    expr.Right.FormattedValue( offset, _indent, _indentLevel )
                );
            }

            // this is the default format, and is used by JOIN, WHERE (non nested), and HAVING criteria
            return String.Format(
                "{0}{1}{2}{3} {4}",
                expr.Left.FormattedValue( offset, _indent, _indentLevel ),
                GetIndent( _indent, _indentLevel ),
                new string( ' ', Math.Max( 0, offset - expr.Operator.Length ) ),
                expr.Operator,
                expr.Right.FormattedValue( offset, _indent, _indentLevel )
            );
        }

        internal string FormatCaseSwitchExpression( Expression expr, int offset )
        {
            if ( CanInlineExpression( expr, offset ) )
                return expr.Value;

            var caseSwitch = (CaseSwitchExpression) expr;
            bool isNested = _indentLevel > 1;

            var sql = new StringBuilder(
                String.Format(
                    "{0}CASE {1}",
                    isNested ? GetIndent( _indent, _indentLevel - 1 ) : "",
                    caseSwitch.Switch.FormattedValue( offset, _indent, _indentLevel )
                )
            );

            foreach ( var caseItem in caseSwitch.Cases )
            {
                sql.Append(
                    String.Format(
                        "{0}WHEN {1} THEN {2}",
                        GetIndent( _indent, _indentLevel + 1 ),
                        caseItem.When.FormattedValue( offset, _indent, _indentLevel ),
                        caseItem.Then.FormattedValue( offset, _indent, _indentLevel )
                    )
                );
            }
            if ( caseSwitch.Else != null )
                sql.Append( FormatCaseElseExpression( offset, caseSwitch, _indentLevel ) );

            sql.Append( GetIndent( _indent, _indentLevel ) + "END" );

            return sql.ToString();
        }

        internal string FormatCaseWhenExpression( Expression expr, int offset )
        {
            if ( CanInlineExpression( expr, offset ) )
                return expr.Value;

            var caseSwitch = (CaseWhenExpression) expr;

            var sql = new StringBuilder(
                String.Format( "{0}CASE", _indentLevel > 1 ? GetIndent( _indent, _indentLevel ) : "" )
            );
            using ( new IndentScope( this ) )
            {
                foreach ( var caseItem in caseSwitch.Cases )
                {
                    sql.AppendFormat( "{0}WHEN ", GetIndent( _indent, _indentLevel ) );
                    sql.AppendFormat( "{0} THEN ", caseItem.When.FormattedValue( offset, _indent, _indentLevel ) );

                    int off = GetCurrentColumn( sql );
                    sql.Append( caseItem.Then.FormattedValue( offset + off, _indent, _indentLevel + 1 ) );
                }
                if ( caseSwitch.Else != null )
                    sql.Append( FormatCaseElseExpression( offset, caseSwitch, _indentLevel - 1 ) );

                sql.Append( GetIndent( _indent, _indentLevel - 1 ) + "END" );

                return sql.ToString();
            }
        }

        internal string FormatNestedExpression( NestedExpression expr, int offset )
        {
            if ( CanInlineExpression( expr.Expression, offset ) )
                return expr.Value;
            else
            {
                StringBuilder sql = new StringBuilder( "(" );
                sql.AppendLine();
                sql.AppendLine();
                sql.Append( GetIndent( _indent, _indentLevel + 1, false ) + expr.Expression.FormattedValue( offset, _indent, _indentLevel ) );
                sql.AppendLine( GetIndent( _indent, _indentLevel - 1 ) );
                sql.Append( GetIndent( _indent, _indentLevel, false ) + ")" );
                return sql.ToString();
            }
        }

        internal string FormatIdentifierListExpression( ExpressionList expr, int offset )
        {
            return GetIndent( _indent, _indentLevel + 1, false ) +
                String.Join( ", ", expr.Identifiers.Select( id => id.FormattedValue( offset, _indent, _indentLevel ) ).ToArray()
            );
        }

        internal string FormatFunctionExpression( FunctionExpression expr, int offset )
        {
            bool isExistsFunction = String.Compare( expr.Name, "EXISTS", true ) == 0;

            string[] args = expr.Arguments
                .Select( arg => arg.FormattedValue( offset, _indent, _indentLevel ) )
                .ToArray();

            bool CanInline = !isExistsFunction && expr.Value.Length <= 40;

            using ( new IndentScope( this ) )
            {
                string prefix = CanInline ? "" : GetIndent( _indent, _indentLevel );
                string postFix = CanInline ? "" : GetIndent( _indent, _indentLevel - 1 );
                string comma = Constants.Comma + (CanInline ? " " : "");
                string separator = !CanInline ? comma + prefix : comma;
                string spacer = isExistsFunction ? "\r\n" : "";

                return String.Format(
                    "{0}({1}{2}{3}{1}{4})",
                    expr.Name,
                    spacer,
                    prefix,
                    String.Join( separator, args ),
                    postFix
                );
            }
        }

        #region IIndentable Members

        public void Indent()
        {
            _indentLevel++;
        }

        public void Unindent()
        {
            _indentLevel--;
        }

        #endregion
    }
}
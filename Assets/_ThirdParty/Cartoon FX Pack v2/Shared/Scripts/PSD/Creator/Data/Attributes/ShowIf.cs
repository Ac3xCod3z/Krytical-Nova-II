using UnityEngine;
using System;
using PSD.Creator.Inspector.Utility;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace PSD.Creator.Data.Attributes
{
    [AttributeUsage(AttributeTargets.Field, Inherited = true, AllowMultiple = false)]
    public class ShowIfAttribute : PropertyAttribute
    {
        public abstract class Condition
        {
            protected readonly bool Invert;

            public Condition(bool invert) => Invert = invert;

#if UNITY_EDITOR
            public bool Show(SerializedProperty property) => Invert ? !Predicate(property) : Predicate(property);
            protected abstract bool Predicate(SerializedProperty property);
#endif
        }
        private class FieldCondition : Condition
        {
            public readonly string Condition;

            public FieldCondition(string condition, bool invert) : base(invert)
            {
                Condition = condition;
            }

#if UNITY_EDITOR
            protected override bool Predicate(SerializedProperty property)
            {
                var conditionProperty = property.FindRelativeProperty(Condition);
                if (conditionProperty != null && conditionProperty.propertyType == SerializedPropertyType.Boolean)
                    return conditionProperty.boolValue;
                return false;
            }
#endif
        }
        private class EnumCondition : Condition
        {
            public readonly string fieldName;
            public readonly int value;

            public EnumCondition(string fieldName, int value, bool invert) : base(invert)
            {
                this.fieldName = fieldName;
                this.value = value;
            }

#if UNITY_EDITOR
            protected override bool Predicate(SerializedProperty property)
            {
                var conditionProperty = property.FindRelativeProperty(fieldName);
                if (conditionProperty != null && conditionProperty.propertyType == SerializedPropertyType.Enum)
                    return conditionProperty.enumValueIndex == value;
                return false;
            }
#endif
        }
        private class FlagEnumCondition : Condition
        {
            public readonly string fieldName;
            public readonly int value;

            public FlagEnumCondition(string fieldName, int value, bool invert) : base(invert)
            {
                this.fieldName = fieldName;
                this.value = value;
            }

#if UNITY_EDITOR
            protected override bool Predicate(SerializedProperty property)
            {
                var conditionProperty = property.FindRelativeProperty(fieldName);
                if (conditionProperty != null && conditionProperty.propertyType == SerializedPropertyType.Enum)
                {
                    int current = conditionProperty.intValue;
                    return (current & value) == value;
                }
                return false;
            }
#endif
        }
        private class ArrayCountCondition : Condition
        {
            public readonly string fieldName;
            public readonly int count;

            public ArrayCountCondition(string fieldName, int count, bool invert) : base(invert)
            {
                this.fieldName = fieldName;
                this.count = count;
            }

#if UNITY_EDITOR
            protected override bool Predicate(SerializedProperty property)
            {
                var conditionProperty = property.FindRelativeProperty(fieldName);
                if (conditionProperty != null && conditionProperty.isArray)
                    return conditionProperty.arraySize > count;
                return false;
            }
#endif
        }
        private class TwoEnumCondition : Condition
        {
            public readonly string fieldName1, fieldName2;
            public readonly int value1, value2;
            public readonly bool isFlag1, isFlag2;
            public readonly bool useOr;

            public TwoEnumCondition(string fieldName1, int value1, string fieldName2, int value2, bool isFlag1, bool isFlag2, bool invert, bool useOr) : base(invert)
            {
                this.fieldName1 = fieldName1; this.value1 = value1;
                this.fieldName2 = fieldName2; this.value2 = value2;
                this.isFlag1 = isFlag1; this.isFlag2 = isFlag2;
                this.useOr = useOr;
            }

#if UNITY_EDITOR
            protected override bool Predicate(SerializedProperty property)
            {
                var p1 = property.FindRelativeProperty(fieldName1);
                var p2 = property.FindRelativeProperty(fieldName2);

                if (p1 == null || p2 == null) return false;
                if (p1.propertyType != SerializedPropertyType.Enum || p2.propertyType != SerializedPropertyType.Enum) return false;

                bool match1 = isFlag1 ? (p1.intValue & value1) == value1 : p1.enumValueIndex == value1;
                bool match2 = isFlag2 ? (p2.intValue & value2) == value2 : p2.enumValueIndex == value2;

                return useOr ? match1 || match2 : match1 && match2;
            }
#endif
        }
        private class EnumAnyOfTwoCondition : Condition
        {
            readonly string fieldName1, fieldName2;
            readonly int[] values;

            public EnumAnyOfTwoCondition(string fieldName1, string fieldName2, int[] values, bool invert) : base(invert)
            {
                this.fieldName1 = fieldName1;
                this.fieldName2 = fieldName2;
                this.values = values;
            }

#if UNITY_EDITOR
            protected override bool Predicate(SerializedProperty property)
            {
                var p1 = property.FindRelativeProperty(fieldName1);
                var p2 = property.FindRelativeProperty(fieldName2);
                if (p1 == null || p2 == null) return false;
                if (p1.propertyType != SerializedPropertyType.Enum || p2.propertyType != SerializedPropertyType.Enum) return false;

                foreach (var v in values)
                    if (p1.enumValueIndex == v || p2.enumValueIndex == v) return true;
                return false;
            }
#endif
        }
        public readonly struct BoolExpr
        {
#if UNITY_EDITOR
            private readonly Func<SerializedProperty, bool> _eval;

            private BoolExpr(Func<SerializedProperty, bool> eval) => _eval = eval;

            public bool Evaluate(SerializedProperty property) => _eval?.Invoke(property) ?? false;

            public static implicit operator BoolExpr(string fieldName) => new BoolExpr(property =>
            {
                var p = property.FindRelativeProperty(fieldName);
                return p?.propertyType == SerializedPropertyType.Boolean && p.boolValue;
            });

            public static BoolExpr operator &(BoolExpr a, BoolExpr b) => new BoolExpr(p => a.Evaluate(p) && b.Evaluate(p));
            public static BoolExpr operator |(BoolExpr a, BoolExpr b) => new BoolExpr(p => a.Evaluate(p) || b.Evaluate(p));
            public static BoolExpr operator !(BoolExpr a) => new BoolExpr(p => !a.Evaluate(p));
#endif
        }
        private class ParsedExprCondition : Condition
        {
            readonly string expr;
            public ParsedExprCondition(string expr, bool invert) : base(invert) => this.expr = expr;

#if UNITY_EDITOR
            protected override bool Predicate(SerializedProperty property)
                => Parse(expr.AsSpan().Trim(), property);

            private static bool Parse(ReadOnlySpan<char> expr, SerializedProperty property)
            {
                while (expr.Length > 0 && expr[0] == '(' && MatchingParen(expr) == expr.Length - 1)
                    expr = expr[1..^1].Trim();

                int depth = 0;
                for (int i = expr.Length - 1; i >= 0; i--)
                {
                    if (expr[i] == ')') depth++;
                    else if (expr[i] == '(') depth--;
                    else if (depth == 0 && expr[i] == '|')
                        return Parse(expr[..i].Trim(), property) | Parse(expr[(i + 1)..].Trim(), property);
                }
                depth = 0;
                for (int i = expr.Length - 1; i >= 0; i--)
                {
                    if (expr[i] == ')') depth++;
                    else if (expr[i] == '(') depth--;
                    else if (depth == 0 && expr[i] == '&')
                        return Parse(expr[..i].Trim(), property) & Parse(expr[(i + 1)..].Trim(), property);
                }

                if (expr[0] == '!')
                    return !Parse(expr[1..].Trim(), property);

                string fieldName = expr.ToString();
                var p = property.FindRelativeProperty(fieldName);
                return p?.propertyType == SerializedPropertyType.Boolean && p.boolValue;
            }

            private static int MatchingParen(ReadOnlySpan<char> expr)
            {
                int depth = 0;
                for (int i = 0; i < expr.Length; i++)
                {
                    if (expr[i] == '(') depth++;
                    else if (expr[i] == ')') { depth--; if (depth == 0) return i; }
                }
                return -1;
            }
#endif
        }
        private class ExprCondition : Condition
        {
            readonly BoolExpr expr;
            public ExprCondition(BoolExpr expr, bool invert) : base(invert) => this.expr = expr;
#if UNITY_EDITOR
            protected override bool Predicate(SerializedProperty property) => expr.Evaluate(property);
#endif
        }
        public readonly struct Expr
        {
            public readonly string Value;
            public Expr(string value) => Value = value;
            public static implicit operator Expr(string value) => new Expr(value);
        }
        public readonly Condition condition;

        public ShowIfAttribute(string field, bool invert = false) => condition = new FieldCondition(field, invert);
        public ShowIfAttribute(string field, int value, bool isFlag = false, bool invert = false) => condition = isFlag
            ? new FlagEnumCondition(field, value, invert) : new EnumCondition(field, value, invert);
        public ShowIfAttribute(string field, int count, bool invert = false) => condition = new ArrayCountCondition(field, count, invert);
        public ShowIfAttribute(string field1, int value1, string field2, int value2, bool isFlag1 = false, bool isFlag2 = false, bool invert = false, bool useOr = false)
            => condition = new TwoEnumCondition(field1, value1, field2, value2, isFlag1, isFlag2, invert, useOr);
        public ShowIfAttribute(string field1, string field2, int[] values, bool invert = false)
            => condition = new EnumAnyOfTwoCondition(field1, field2, values, invert);
        public ShowIfAttribute(params string[] expr) => condition = new ParsedExprCondition(string.Join(" ", expr), invert: false);
    }
}
namespace JaskLang;

using System.Text;

public partial class Interpreter
{
    private static string FunctionKey(string name, IEnumerable<string> paramTypes)
        => $"{name}({string.Join(",", paramTypes)})";

    private static string FunctionKey(string name, List<(Token Name, Token Type)> parameters)
        => FunctionKey(name, parameters.Select(p => p.Type.Lexeme));
    
    private static bool IsValueOfType(object? value, string typeName)
    {
        return typeName.ToLower() switch
        {
            "number"  => value is double,
            "string"  => value is string,
            "boolean" => value is bool,
            "list"    => value is List<object?>,
            "any"     => value is object || value == null,
            _ => value is StructInstance si && si.TypeName == typeName
        };
    }

    private static string GetValueType(object? value)
    {
        return value switch
        {
            double => "number",
            string => "string",
            bool   => "boolean",
            List<object?> => "list",
            StructInstance si => si.TypeName,
            null => "nil",
            _ => value.GetType().Name
        };
    }

    private static bool AreValuesEqual(object? a, object? b)
    {
        if (a is null && b is null)
        {
            return true;
        }

        if (a is null || b is null)
        {
            return false;
        }

        if (a.GetType() != b.GetType())
        {
            return false;
        }

        if (a is StructInstance sa && b is StructInstance sb)
        {
            return AreStructInstancesEqual(sa, sb);
        }

        if (a is List<object?> la && b is List<object?> lb)
        {
            if (la.Count != lb.Count)
            {
                return false;
            }

            for (int i = 0; i < la.Count; i++)
            {
                if (!AreValuesEqual(la[i], lb[i]))
                {
                    return false;
                }
            }

            return true;
        }

        return a.Equals(b);
    }

    private static bool AreStructInstancesEqual(StructInstance left, StructInstance right)
    {
        if (left.TypeName != right.TypeName)
        {
            return false;
        }

        if (left.Fields.Count != right.Fields.Count)
        {
            return false;
        }

        foreach (var (key, leftFieldValue) in left.Fields)
        {
            if (!right.Fields.TryGetValue(key, out var rightFieldValue))
            {
                return false;
            }

            if (!AreValuesEqual(leftFieldValue, rightFieldValue))
            {
                return false;
            }
        }

        return true;
    }

    private double CheckNumber(Token op, object? value)
    {
        if (value is double d)
        {
            return d;
        }

        throw new LangException($"Operator '{op.Lexeme}' expects a number, but received '{Stringify(value)}'", op.Line, _filePath);
    }

    private double CheckNumberStmt(Token context, object? value, string label)
    {
        if (value is double d)
        {
            return d;
        }

        throw new LangException($"{label} must be a number, but is a '{Stringify(value)}'", context.Line, _filePath);
    }

    public static string Stringify(object? value)
    {
        if (value is null) return "nil";
        if (value is bool b) return b ? "true" : "false";
        if (value is StructInstance si) return si.ToString();

        if (value is double d)
        {
            // trim integers (4 instead of 4.0)
            if (d >= long.MinValue && d <= long.MaxValue && d == Math.Floor(d))
            {
                return ((long)d).ToString();
            }

            return d.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        if (value is List<object?> list)
        {
            StringBuilder result = new StringBuilder("[");
            for (int i = 0; i < list.Count; i++)
            {
                var ele = list[i];
                if (i > 0)
                {
                    result.Append(", ");
                }

                if (ele is string str)
                {
                    result.Append("\"" + str + "\"");
                }
                else
                {
                    result.Append(Stringify(ele));
                }
            }

            result.Append("]");
            return result.ToString();
        }

        return value.ToString() ?? "nil";
    }

    private List<string> GetInternalFunctionParameterNames(string funcName)
    {
        return funcName switch
        {
            "exit"     => new() { "code" },
            "assert"   => new() { "condition" },
            "sleepFor" => new() { "seconds" },
            "type"     => new() { "variable" },

            "round" => new() { "number" },
            "floor" => new() { "number" },
            "ceil"  => new() { "number" },

            "stringGetIndexOf"   => new() { "string", "substring" },
            "stringGetSubstring" => new() { "string", "index", "length" },

            "toNumber" => new() { "value" },
            "toString" => new() { "value" },

            "readInput" => new() { "prompt" },
            "readFile"  => new() { "file" },

            "listSize"     => new() { "list" },
            "listAdd"      => new() { "list", "element" },
            "listGet"      => new() { "list", "index" },
            "listGetRange" => new() { "list", "indexStart", "indexEnd" },
            "listSet"      => new() { "list", "index", "element" },
            "listRemove"   => new() { "list", "index" },
            "listReverse"  => new() { "list" },
            "listExtend"   => new() { "list", "expander" },

            _ => throw new LangException($"Function '{funcName}' does not support named parameters", new Token(TokenType.Identifier, funcName, null, 0).Line, _filePath)
        };
    }
}
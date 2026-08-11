using System.Text;

namespace JaskLang;

public partial class Interpreter
{
    private void initInternalFunctionsModule()
    {
        RegisterInternalFunction("unfoldModule", new List<(string, string)> { ("alias", "any") }, CallInternalFunctionUnfoldModule);
    }

    private object? CallInternalFunctionUnfoldModule(Expression.Call call)
    {
        CheckNumberOfArguments(call, 1, "unfoldModule");

        string? moduleName = null;

        // if the argument is a variable name that matches a module alias, use it directly
        if (call.Arguments[0] is Expression.Variable varExpr && _modules.ContainsKey(varExpr.Name.Lexeme))
        {
            moduleName = varExpr.Name.Lexeme;
        }
        else
        {
            object? moduleNameObj = Evaluate(call.Arguments[0]);

            if (moduleNameObj is string name && _modules.ContainsKey(name))
            {
                moduleName = name;
            }
        }

        if (moduleName == null)
        {
            object? moduleNameObj = Evaluate(call.Arguments[0]);
            throw new LangException($"Function 'unfoldModule' expects a module alias or a string matching a module alias, but got '{GetValueType(moduleNameObj)}'", GetCallToken(call).Line, _filePath);
        }

        Interpreter module = _modules[moduleName];
        StringBuilder builder = new StringBuilder();

        builder.Append("--- Loaded modules ---\n");
        if (module._modules.Count() == 0)
        {
            builder.Append('/');
        }
        else
        {
            foreach (var modl in module._modules)
            {
                builder.Append(modl.Key + "\n");
            }

            // remove last \n from string
            builder.Remove(builder.Length - 1, 1);
        }
        builder.Append("\n");

        builder.Append("--- Struct definitions ---\n");
        if (module._structs.Count() == 0)
        {
            builder.Append('/');
        }
        else
        {
            foreach (var str in module._structs)
            {
                builder.Append(str.Key + "\n");
            }

            // remove last \n from string
            builder.Remove(builder.Length - 1, 1);
        }
        builder.Append("\n");

        builder.Append("--- Function definitions ---\n");
        bool hasAnyFunctions = false;

        // user defined functions
        if (module._functions.Count > 0)
        {
            hasAnyFunctions = true;
            foreach (var func in module._functions)
            {
                builder.Append(func.Key.Split('(')[0]);
                builder.Append("(");

                for (int i = 0; i < func.Value.Params.Count; i++)
                {
                    var param = func.Value.Params[i];

                    // we do not want to evaluate every possible expression here... so only showing literals
                    string? def = param.Default switch
                    {
                        Expression.Literal lit => Stringify(lit.Value),
                        Expression.Variable => "*variable*",
                        Expression.Unary => "*expression*",
                        Expression.Binary => "*expression*",
                        Expression.Grouping => "*expression*",
                        Expression.Call => "*function call*",
                        Expression.NamedCall => "*function call*",
                        Expression.ModuleCall => "*function call*",
                        Expression.ModuleNamedCall => "*function call*",
                        Expression.StructCall => "*expression*",
                        Expression.MemberAccess => "*expression*",
                        Expression.MapLiteral => "*expression*",
                        Expression.MapIndex => "*expression*",
                        Expression.ListLiteral => "*expression*",
                        _ => null
                    };

                    builder.Append(param.Name.Lexeme + ": " + param.Type.Lexeme + (def != null ? " = " + def : ""));

                    if (i != func.Value.Params.Count - 1)
                    {
                        builder.Append(", ");
                    }
                }

                builder.Append(")\n");
            }

            // remove last \n from string
            builder.Remove(builder.Length - 1, 1);
        }

        // internal module group functions (e.g jcore/http::get etc.)
        if (module._internalFunctionModuleGroups.Count > 0)
        {
            hasAnyFunctions = true;
            var allFuncNames = module._internalFunctionModuleGroups.SelectMany(g => g.Value).Distinct().ToList();
            foreach (var funcName in allFuncNames)
            {
                builder.Append(funcName);
                builder.Append("(");

                if (module._internalFunctionParamNames.TryGetValue(funcName, out var paramsWithTypes) && paramsWithTypes.Count > 0)
                {
                    for (int i = 0; i < paramsWithTypes.Count; i++)
                    {
                        builder.Append(paramsWithTypes[i].Name + ": " + paramsWithTypes[i].Type);
                        if (i != paramsWithTypes.Count - 1)
                        {
                            builder.Append(", ");
                        }
                    }
                }

                builder.Append(")\n");
            }

            // remove last \n from string
            builder.Remove(builder.Length - 1, 1);
        }

        if (!hasAnyFunctions)
        {
            builder.Append('/');
        }

        return builder.ToString();
    }
}
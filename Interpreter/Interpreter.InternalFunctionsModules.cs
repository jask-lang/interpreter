using System.Text;

namespace JaskLang;

public partial class Interpreter
{
    private void initInternalFunctionsModule()
    {
        _internalFunctions["unfoldModule"] = CallInternalFunctionUnfoldModule;
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
        
        foreach (var func in module._functions)
        {
            builder.Append(func.Key + "\n");
        }

        // remove last \n from string
        builder.Remove(builder.Length - 1, 1);

        return builder.ToString();
    }
}
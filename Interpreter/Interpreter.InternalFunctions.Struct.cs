namespace JaskLang;

public partial class Interpreter
{
    private void initInternalFunctionsStruct()
    {
        RegisterInternalFunction("getFields", new List<(string, string)> { ("struct", "struct") },                      CallInternalFunctionGetFields);
        RegisterInternalFunction("hasField",  new List<(string, string)> { ("struct", "struct"), ("field", "string") }, CallInternalFunctionHasField);
    }

    private object? CallInternalFunctionGetFields(Expression.Call call)
    {
        CheckNumberOfArguments(call, 1, "getFields");

        object? structObj = Evaluate(call.Arguments[0]);

        if (structObj is not StructInstance structValue)
        {
            throw new LangException($"Function 'getFields' expets a struct instance, but got '{structObj}'");
        }

        return structValue.getFieldNames();
    }

    private object? CallInternalFunctionHasField(Expression.Call call)
    {
        CheckNumberOfArguments(call, 2, "hasField");

        object? structObj = Evaluate(call.Arguments[0]);

        if (structObj is not StructInstance structValue)
        {
            throw new LangException($"Function 'hasField' expects a struct instance, but got '{structObj}'");
        }

        object? lookupObj = Evaluate(call.Arguments[1]);

        if (lookupObj is not string lookup)
        {
            throw new LangException($"Function 'hasField' expects a string, but got '{lookupObj}'");
        }

        return structValue.Fields.ContainsKey(lookup);
    }
}
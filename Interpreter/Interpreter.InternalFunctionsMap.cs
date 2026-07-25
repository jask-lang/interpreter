namespace JaskLang;

public partial class Interpreter
{
    private void initInternalFunctionsMap()
    {
        _internalFunctions["map"] = CallInternalFunctionMap;
    }

    private object? CallInternalFunctionMap(Expression.Call call)
    {
        CheckNumberOfArguments(call, 2, "map");

        object? keysObj = Evaluate(call.Arguments[0]);

        if (keysObj is not List<object?> keys)
        {
            throw new LangException($"Function 'map' expects a list, but got '{GetValueType(keysObj)}'", GetCallToken(call).Line, _filePath);
        }

        if (keys.Contains(null) == true)
        {
            throw new LangException($"Function 'map' does not allow 'nil' as key", GetCallToken(call).Line, _filePath);
        }

        HashSet<object?> seen = new HashSet<object?>(keys.Count);
        foreach (var item in keys)
        {
            if (seen.Add(item) == false)
            {
                throw new LangException($"Function 'map' expects a unique list of keys. Value '{item}' is not unique in key list", GetCallToken(call).Line, _filePath);
            }
        }

        object? valuesObj = Evaluate(call.Arguments[1]);

        if (valuesObj is not List<object?> values)
        {
            throw new LangException($"Function 'map' expects a list, but got '{GetValueType(valuesObj)}'", GetCallToken(call).Line, _filePath);
        }

        if (keys.Count != values.Count)
        {
            throw new LangException($"Function 'map' expects the same amount of keys and values", GetCallToken(call).Line, _filePath);
        }

        if (values.Contains(null) == true)
        {
            throw new LangException($"Function 'map' does not allow 'nil' as value for key", GetCallToken(call).Line, _filePath);
        }

        var map = new Dictionary<object, object>(keys.Count);

        for (int i = 0; i < keys.Count; i++)
        {
            map[keys[i]!] = values[i]!;
        }

        return map;
    }
}
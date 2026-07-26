namespace JaskLang;

public partial class Interpreter
{
    private void initInternalFunctionsMap()
    {
        _internalFunctions["map"]          = CallInternalFunctionMap;
        _internalFunctions["mapGet"]       = CallInternalFunctionMapGet;
        _internalFunctions["mapSet"]       = CallInternalFunctionMapSet;
        _internalFunctions["mapGetKeys"]   = CallInternalFunctionMapGetKeys;
        _internalFunctions["mapGetValues"] = CallInternalFunctionMapGetValues;
        _internalFunctions["mapHasKey"]    = CallInternalFunctionMapHasKey;
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

    private object? CallInternalFunctionMapGet(Expression.Call call)
    {
        CheckNumberOfArguments(call, 2, "mapGet");

        object? mapObj = Evaluate(call.Arguments[0]);

        if (mapObj is not Dictionary<object, object> map)
        {
            throw new LangException($"Function 'mapGet' expects a map, but got '{GetValueType(mapObj)}'", GetCallToken(call).Line, _filePath);
        }

        object? key = Evaluate(call.Arguments[1]);

        if (key is null)
        {
            throw new LangException($"Function 'mapGet' cannot use 'nil' as key", GetCallToken(call).Line, _filePath);
        }

        if (map.ContainsKey(key) == false)
        {
            throw new LangException($"Function 'mapGet' cannot find key '{key}'", GetCallToken(call).Line, _filePath);
        }

        return map[key];
    }

    private object? CallInternalFunctionMapSet(Expression.Call call)
    {
        CheckNumberOfArguments(call, 3, "mapSet");

        object? mapObj = Evaluate(call.Arguments[0]);

        if (mapObj is not Dictionary<object, object> map)
        {
            throw new LangException($"Function 'mapSet' expects a map, but got '{GetValueType(mapObj)}'", GetCallToken(call).Line, _filePath);
        }

        object? key = Evaluate(call.Arguments[1]);

        if (key is null)
        {
            throw new LangException($"Function 'mapSet' cannot use 'nil' as key", GetCallToken(call).Line, _filePath);
        }

        object? value = Evaluate(call.Arguments[2]);

        if (value is null)
        {
            throw new LangException($"Function 'mapSet' cannot use 'nil' as value", GetCallToken(call).Line, _filePath);
        }

        map[key] = value;

        return map;
    }

    private object? CallInternalFunctionMapGetKeys(Expression.Call call)
    {
        CheckNumberOfArguments(call, 1, "mapGetKeys");

        object? mapObj = Evaluate(call.Arguments[0]);

        if (mapObj is not Dictionary<object, object> map)
        {
            throw new LangException($"Function 'mapGetKeys' expects a map, but got '{GetValueType(mapObj)}'", GetCallToken(call).Line, _filePath);
        }

        return map.Keys.ToList();
    }

    private object? CallInternalFunctionMapGetValues(Expression.Call call)
    {
        CheckNumberOfArguments(call, 1, "mapGetValues");

        object? mapObj = Evaluate(call.Arguments[0]);

        if (mapObj is not Dictionary<object, object> map)
        {
            throw new LangException($"Function 'mapGetValues' expects a map, but got '{GetValueType(mapObj)}'", GetCallToken(call).Line, _filePath);
        }

        return map.Values.ToList();
    }

    private object? CallInternalFunctionMapHasKey(Expression.Call call)
    {
        CheckNumberOfArguments(call, 2, "mapHasKey");

        object? mapObj = Evaluate(call.Arguments[0]);

        if (mapObj is not Dictionary<object, object> map)
        {
            throw new LangException($"Function 'mapHasKey' expects a map, but got '{GetValueType(mapObj)}'", GetCallToken(call).Line, _filePath);
        }

        object? key = Evaluate(call.Arguments[1]);

        if (key is null)
        {
            throw new LangException($"Function 'mapHasKey' cannot use 'nil' as key", GetCallToken(call).Line, _filePath);
        }

        return map.ContainsKey(key);
    }
}
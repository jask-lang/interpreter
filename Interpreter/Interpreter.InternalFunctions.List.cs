namespace JaskLang;

public partial class Interpreter
{
    private void initInternalFunctionsList()
    {
        RegisterInternalFunction("list",                                                                                                               CallInternalFunctionList);
        RegisterInternalFunction("listSize",        new List<(string, string)> { ("list", "list") },                                                   CallInternalFunctionListSize);
        RegisterInternalFunction("listAdd",         new List<(string, string)> { ("list", "list"), ("element", "any") },                               CallInternalFunctionListAdd);
        RegisterInternalFunction("listGet",         new List<(string, string)> { ("list", "list"), ("index", "number") },                              CallInternalFunctionListGet);
        RegisterInternalFunction("listGetRange",    new List<(string, string)> { ("list", "list"), ("indexStart", "number"), ("indexEnd", "number") }, CallInternalFunctionListGetRange);
        RegisterInternalFunction("listSet",         new List<(string, string)> { ("list", "list"), ("index", "number"), ("element", "any") },          CallInternalFunctionListSet);
        RegisterInternalFunction("listRemove",      new List<(string, string)> { ("list", "list"), ("index", "number") },                              CallInternalFunctionListRemove);
        RegisterInternalFunction("listReverse",     new List<(string, string)> { ("list", "list") },                                                   CallInternalFunctionListReverse);
        RegisterInternalFunction("listExtend",      new List<(string, string)> { ("list", "list"), ("elements", "list") },                             CallInternalFunctionListExtend);
        RegisterInternalFunction("listCreateRange", new List<(string, string)> { ("start", "number"), ("end", "number") },                             CallInternalFunctionListCreateRange);
    }

    private object? CallInternalFunctionList(Expression.Call call)
    {
        var list = new List<object?>();

        foreach (var arg in call.Arguments)
        {
            list.Add(Evaluate(arg));
        }

        return list;
    }

    private object? CallInternalFunctionListSize(Expression.Call call)
    {
        CheckNumberOfArguments(call, 1, "listSize");

        object? listObj = Evaluate(call.Arguments[0]);

        if (listObj is not List<object?> list)
        {
            throw new LangException($"Function 'listSize' expects a list, but got '{GetValueType(listObj)}'", GetCallToken(call).Line, _filePath);
        }

        return (double)list.Count;
    }

    private object? CallInternalFunctionListAdd(Expression.Call call)
    {
        CheckNumberOfArguments(call, 2, "listAdd");

        object? listObj = Evaluate(call.Arguments[0]);

        if (listObj is not List<object?> list)
        {
            throw new LangException($"Function 'listAdd' expects first argument to be a list, but got '{GetValueType(listObj)}'", GetCallToken(call).Line, _filePath);
        }

        // create a copy of the list to avoid modifying the original
        var newList = list.ToList();
        newList.Add(Evaluate(call.Arguments[1]));

        return newList;
    }

    private object? CallInternalFunctionListGet(Expression.Call call)
    {
        CheckNumberOfArguments(call, 2, "listGet");

        object? indexObj = Evaluate(call.Arguments[1]);
        if (indexObj is not double indexDouble)
        {
            throw new LangException($"Function 'listGet' expects second argument to be a number, but got '{GetValueType(indexObj)}'", GetCallToken(call).Line, _filePath);
        }

        int index = (int)indexDouble;

        object? listObj = Evaluate(call.Arguments[0]);

        if (listObj is not List<object?> list)
        {
            throw new LangException($"Function 'listGet' expects first argument to be a list, but got '{GetValueType(listObj)}'", GetCallToken(call).Line, _filePath);
        }

        if (index < 0 || index >= list.Count)
        {
            throw new LangException($"Function 'listGet' index {index} is out of bounds for list of size {list.Count}", GetCallToken(call).Line, _filePath);
        }

        return list[index];
    }

    private object? CallInternalFunctionListGetRange(Expression.Call call)
    {
        CheckNumberOfArguments(call, 3, "listGetRange");

        object? startIndexObj = Evaluate(call.Arguments[1]);
        if (startIndexObj is not double startIndexDouble)
        {
            throw new LangException($"Function 'listGetRange' expects second argument to be a number, but got '{GetValueType(startIndexObj)}'", GetCallToken(call).Line, _filePath);
        }

        object? endIndexObj = Evaluate(call.Arguments[2]);
        if (endIndexObj is not double endIndexDouble)
        {
            throw new LangException($"Function 'listGetRange' expects third argument to be a number, but got '{GetValueType(endIndexObj)}'", GetCallToken(call).Line, _filePath);
        }

        int startIndex = (int)startIndexDouble;
        int endIndex = (int)endIndexDouble;

        object? listObj = Evaluate(call.Arguments[0]);

        if (listObj is not List<object?> list)
        {
            throw new LangException($"Function 'listGetRange' expects first argument to be a list, but got '{GetValueType(listObj)}'", GetCallToken(call).Line, _filePath);
        }

        if (startIndex < 0 || endIndex >= list.Count || startIndex > endIndex)
        {
            throw new LangException($"Function 'listGetRange' indices [{startIndex}, {endIndex}] are out of bounds for list of size {list.Count}", GetCallToken(call).Line, _filePath);
        }

        return list.GetRange(startIndex, endIndex - startIndex + 1);
    }

    private object? CallInternalFunctionListSet(Expression.Call call)
    {
        CheckNumberOfArguments(call, 3, "listSet");

        object? indexObj = Evaluate(call.Arguments[1]);
        if (indexObj is not double indexDouble)
        {
            throw new LangException($"Function 'listSet' expects second argument to be a number, but got '{GetValueType(indexObj)}'", GetCallToken(call).Line, _filePath);
        }

        int index = (int)indexDouble;

        object? listObj = Evaluate(call.Arguments[0]);
        object? value = Evaluate(call.Arguments[2]);

        if (listObj is not List<object?> list)
        {
            throw new LangException($"Function 'listSet' expects first argument to be a list, but got '{GetValueType(listObj)}'", GetCallToken(call).Line, _filePath);
        }

        if (index < 0 || index >= list.Count)
        {
            throw new LangException($"Function 'listSet' index {index} is out of bounds for list of size {list.Count}", GetCallToken(call).Line, _filePath);
        }

        // create a copy of the list to avoid modifying the original
        var newList = list.ToList();
        newList[index] = value;

        return newList;
    }

    private object? CallInternalFunctionListRemove(Expression.Call call)
    {
        CheckNumberOfArguments(call, 2, "listRemove");

        object? indexObj = Evaluate(call.Arguments[1]);
        if (indexObj is not double indexDouble)
        {
            throw new LangException($"Function 'listRemove' expects second argument to be a number, but got '{GetValueType(indexObj)}'", GetCallToken(call).Line, _filePath);
        }

        int index = (int)indexDouble;

        object? listObj = Evaluate(call.Arguments[0]);

        if (listObj is not List<object?> list)
        {
            throw new LangException($"Function 'listRemove' expects first argument to be a list, but got '{GetValueType(listObj)}'", GetCallToken(call).Line, _filePath);
        }

        if (index < 0 || index >= list.Count)
        {
            throw new LangException($"Function 'listRemove' index {index} is out of bounds for list of size {list.Count}", GetCallToken(call).Line, _filePath);
        }

        // create a copy of the list to avoid modifying the original
        var newList = list.ToList();
        newList.RemoveAt(index);

        return newList;
    }

    private object? CallInternalFunctionListReverse(Expression.Call call)
    {
        CheckNumberOfArguments(call, 1, "listReverse");

        object? listObj = Evaluate(call.Arguments[0]);

        if (listObj is not List<object?> list)
        {
            throw new LangException($"Function 'listReverse' expects first argument to be a list, but got '{GetValueType(listObj)}'", GetCallToken(call).Line, _filePath);
        }

        // create a copy of the list to avoid modifying the original
        var newList = list.ToList();
        newList.Reverse();

        return newList;
    }

    private object? CallInternalFunctionListExtend(Expression.Call call)
    {
        CheckNumberOfArguments(call, 2, "listExtend");

        object? listObj1 = Evaluate(call.Arguments[0]);
        object? listObj2 = Evaluate(call.Arguments[1]);

        if (listObj1 is List<object?> list1)
        {
            if (listObj2 is List<object?> list2)
            {
                // create a copy of the first list to avoid modifying the original
                var newList = list1.ToList();
                newList.AddRange(list2);

                return newList;
            }
        }

        if (listObj1 is string str1)
        {
            if (listObj2 is string str2)
            {
                return str1 + str2;
            }
        }

        throw new LangException($"Function 'listExtend' expects both arguments to be lists or both to be strings, but got '{GetValueType(listObj1)}' and '{GetValueType(listObj2)}'", GetCallToken(call).Line, _filePath);
    }

    private object? CallInternalFunctionListCreateRange(Expression.Call call)
    {
        CheckNumberOfArguments(call, 2, "listCreateRange");

        object? rangeStartObj = Evaluate(call.Arguments[0]);
        object? rangeEndObj = Evaluate(call.Arguments[1]);

        if (rangeStartObj is not double rangeStart)
        {
            throw new LangException($"Function 'listCreateRange' expects first argument to be a number, but got '{GetValueType(rangeStartObj)}'", GetCallToken(call).Line, _filePath);
        }

        if (rangeEndObj is not double rangeEnd)
        {
            throw new LangException($"Function 'listCreateRange' expects second argument to be a number, but got '{GetValueType(rangeEndObj)}'", GetCallToken(call).Line, _filePath);
        }

        // Calculate total element count upfront
        int count = (int)Math.Floor(Math.Abs(rangeEnd - rangeStart) / 1) + 1;

        // pre-allocate the internal array buffer
        List<object?> range = new List<object?>(count);

        if (rangeEnd < rangeStart)
        {
            for (double i = rangeStart; i >= rangeEnd; i -= 1)
            {
                range.Add(i);
            }
        }
        else
        {
            for (double i = rangeStart; i <= rangeEnd; i += 1)
            {
                range.Add(i);
            }
        }

        return range;
    }
}
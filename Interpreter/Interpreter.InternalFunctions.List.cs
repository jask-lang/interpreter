namespace JaskLang;

public partial class Interpreter
{
    private void initInternalFunctionsList()
    {
        _internalFunctions["list"]         = CallInternalFunctionList;
        _internalFunctions["listSize"]     = CallInternalFunctionListSize;
        _internalFunctions["listAdd"]      = CallInternalFunctionListAdd;
        _internalFunctions["listGet"]      = CallInternalFunctionListGet;
        _internalFunctions["listGetRange"] = CallInternalFunctionListGetRange;
        _internalFunctions["listSet"]      = CallInternalFunctionListSet;
        _internalFunctions["listRemove"]   = CallInternalFunctionListRemove;
        _internalFunctions["listReverse"]  = CallInternalFunctionListReverse;
        _internalFunctions["listExtend"]   = CallInternalFunctionListExtend;
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

        if (listObj is string listString)
        {
            return (double)listString.Length;
        }

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

        if (listObj is List<object?> list)
        {
            // create a copy of the list to avoid modifying the original
            var newList = list.ToList();
            newList.Add(Evaluate(call.Arguments[1]));

            return newList;
        }

        if (listObj is string str)
        {
            object? elementToAdd = Evaluate(call.Arguments[1]);
            if (elementToAdd is not string)
            {
                throw new LangException($"Function 'listAdd' expects second argument to be a string when first argument is a string, but got '{GetValueType(elementToAdd)}'", GetCallToken(call).Line, _filePath);
            }

            return str + (string)elementToAdd;
        }

        throw new LangException($"Function 'listAdd' expects first argument to be a list or string, but got '{GetValueType(listObj)}'", GetCallToken(call).Line, _filePath);
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

        if (listObj is List<object?> list)
        {
            if (index < 0 || index >= list.Count)
            {
                throw new LangException($"Function 'listGet' index {index} is out of bounds for list of size {list.Count}", GetCallToken(call).Line, _filePath);
            }

            return list[index];
        }

        if (listObj is string str)
        {
            if (index < 0 || index >= str.Length)
            {
                throw new LangException($"Function 'listGet' index {index} is out of bounds for string of length {str.Length}", GetCallToken(call).Line, _filePath);
            }

            return str[index].ToString();
        }

        throw new LangException($"Function 'listGet' expects first argument to be a list or a a string, but got '{GetValueType(listObj)}'", GetCallToken(call).Line, _filePath);
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
        
        if (listObj is List<object?> list)
        {
            if (startIndex < 0 || endIndex >= list.Count || startIndex > endIndex)
            {
                throw new LangException($"Function 'listGetRange' indices [{startIndex}, {endIndex}] are out of bounds for list of size {list.Count}", GetCallToken(call).Line, _filePath);
            }

            return list.GetRange(startIndex, endIndex - startIndex + 1);
        }

        if (listObj is string str)
        {
            if (startIndex < 0 || endIndex >= str.Length || startIndex > endIndex)
            {
                throw new LangException($"Function 'listGetRange' indices [{startIndex}, {endIndex}] are out of bounds for string of length {str.Length}", GetCallToken(call).Line, _filePath);
            }

            return str.Substring(startIndex, endIndex - startIndex + 1);
        }

        throw new LangException($"Function 'listGetRange' expects first argument to be a list or a string, but got '{GetValueType(listObj)}'", GetCallToken(call).Line, _filePath);
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

        if (listObj is List<object?> list)
        {
            if (index < 0 || index >= list.Count)
            {
                throw new LangException($"Function 'listSet' index {index} is out of bounds for list of size {list.Count}", GetCallToken(call).Line, _filePath);
            }

            // create a copy of the list to avoid modifying the original
            var newList = list.ToList();
            newList[index] = value;

            return newList;
        }

        if (listObj is string str)
        {
            
            if (index < 0 || index >= str.Length)
            {
                throw new LangException($"Function 'listSet' index {index} is out of bounds for string of length {str.Length}", GetCallToken(call).Line, _filePath);
            }

            if (value is not string)
            {
                throw new LangException($"Function 'listSet' expects third argument to be a string when first argument is a string, but got '{GetValueType(value)}'", GetCallToken(call).Line, _filePath);
            }

            // create a new string with the character at the specified index replaced
            var newStr = str.Substring(0, index) + value?.ToString() + str.Substring(index + 1);
            return newStr;
        }

        throw new LangException($"Function 'listSet' expects first argument to be a list or a string, but got '{GetValueType(listObj)}'", GetCallToken(call).Line, _filePath);
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

        if (listObj is List<object?> list)
        {
            if (index < 0 || index >= list.Count)
            {
                throw new LangException($"Function 'listRemove' index {index} is out of bounds for list of size {list.Count}", GetCallToken(call).Line, _filePath);
            }

            // create a copy of the list to avoid modifying the original
            var newList = list.ToList();
            newList.RemoveAt(index);

            return newList;
        }

        if (listObj is string str)
        {
            if (index < 0 || index >= str.Length)
            {
                throw new LangException($"Function 'listRemove' index {index} is out of bounds for string of length {str.Length}", GetCallToken(call).Line, _filePath);
            }

            // create a new string with the character at the specified index removed
            var newStr = str.Substring(0, index) + str.Substring(index + 1);
            return newStr;
        }

        throw new LangException($"Function 'listRemove' expects first argument to be a list or a string, but got '{GetValueType(listObj)}'", GetCallToken(call).Line, _filePath);
    }

    private object? CallInternalFunctionListReverse(Expression.Call call)
    {
        CheckNumberOfArguments(call, 1, "listReverse");

        object? listObj = Evaluate(call.Arguments[0]);

        if (listObj is List<object?> list)
        {
            // create a copy of the list to avoid modifying the original
            var newList = list.ToList();
            newList.Reverse();

            return newList;
        }

        if (listObj is string str)
        {
            char[] charArray = str.ToCharArray();
            Array.Reverse(charArray);
            return new string(charArray);
        }

        throw new LangException($"Function 'listReverse' expects first argument to be a list or a string, but got '{GetValueType(listObj)}'", GetCallToken(call).Line, _filePath);
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
}
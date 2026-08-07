namespace JaskLang;

public partial class Interpreter
{
    public delegate object? InternalFunctionDelegate(Expression.Call call);

    // dictionary for internal functions: name -> delegate
    private readonly Dictionary<string, InternalFunctionDelegate> _internalFunctions = [];

    private void initInternalFunctions()
    {
        // standard functions
        _internalFunctions["print"]     = CallInternalFunctionPrint;
        _internalFunctions["printLine"] = CallInternalFunctionPrintLine;
        _internalFunctions["type"]      = CallInternalFunctionType;
        _internalFunctions["clock"]     = CallInternalFunctionClock;
        _internalFunctions["exit"]      = CallInternalFunctionExit;
        _internalFunctions["assert"]    = CallInternalFunctionAssert;
        _internalFunctions["sleepFor"]  = CallInternalFunctionSleepFor;

        // variable convertions
        _internalFunctions["toNumber"] = CallInternalFunctionToNumber;
        _internalFunctions["toString"] = CallInternalFunctionToString;

        // math functions
        _internalFunctions["round"] = CallInternalFunctionRound;
        _internalFunctions["floor"] = CallInternalFunctionFloor;
        _internalFunctions["ceil"]  = CallInternalFunctionCeil;

        // string functions
        _internalFunctions["charCode"]           = CallInternalFunctionCharCode;
        _internalFunctions["charFromCode"]       = CallInternalFunctionCharFromCode;
        _internalFunctions["charToUpper"]        = CallInternalFunctionCharToUpper;
        _internalFunctions["charToLower"]        = CallInternalFunctionCharToLower;
        _internalFunctions["charCount"]          = CallInternalFunctionCharCount;
        _internalFunctions["charAt"]             = CallInternalFunctionCharAt;

        // list functions
        initInternalFunctionsList();

        // map functions
        initInternalFunctionsMap();

        // module functions
        initInternalFunctionsModule();

        // trust engine
        initInternalFunctionsTrustEngine();

        // struct functions
        initInternalFunctionsStruct();

        // IO functions
        initInternalFunctionsIO();
    }

    private Token GetCallToken(Expression.Call call) => ((Expression.Variable)call.Callee).Name;
    
    private void CheckNumberOfArguments(Expression.Call call, int expected, string funcName)
    {
        if (call.Arguments.Count != expected)
        {
            throw new LangException($"Function '{funcName}' expects {expected} argument(s), but got {call.Arguments.Count}", GetCallToken(call).Line, _filePath);
        }
    }

    private object? CallInternalFunctionPrint(Expression.Call call)
    {
        if (_permissionManager.IsPermitted(Permission.Stdout) == false)
        {
            throw new LangException($"Missing permission 'stdout' for function 'print'", GetCallToken(call).Line, _filePath);
        }

        // check number of arguments (print accepts at least 1)
        CheckNumberOfArguments(call, call.Arguments.Count, "print");

        // print all arguments
        var parts = new List<string>();
        foreach (var arg in call.Arguments)
        {
            parts.Add(Stringify(Evaluate(arg)));
        }

        Console.Write(string.Join("", parts));

        return null;
    }

    private object? CallInternalFunctionPrintLine(Expression.Call call)
    {
        if (_permissionManager.IsPermitted(Permission.Stdout) == false)
        {
            throw new LangException($"Missing permission 'stdout' for function 'printLine'", GetCallToken(call).Line, _filePath);
        }

        // check number of arguments (printLine accepts at least 1)
        CheckNumberOfArguments(call, call.Arguments.Count, "printLine");

        // print all arguments
        var parts = new List<string>();
        foreach (var arg in call.Arguments)
        {
            parts.Add(Stringify(Evaluate(arg)));
        }

        Console.Write(string.Join("", parts));
        Console.WriteLine();

        return null;
    }

    private object? CallInternalFunctionType(Expression.Call call)
    {
        CheckNumberOfArguments(call, 1, "type");
        
        object? value = Evaluate(call.Arguments[0]);

        return GetValueType(value);
    }

    private object? CallInternalFunctionRound(Expression.Call call)
    {
        CheckNumberOfArguments(call, 2, "round");

        object? number = Evaluate(call.Arguments[0]);
        if (number is not double d)
        {
            throw new LangException($"Function 'round' expects a number argument, but got '{GetValueType(number)}'", GetCallToken(call).Line, _filePath);
        }

        object? digits = Evaluate(call.Arguments[1]);
        if (digits is not double digitsDouble)
        {
            throw new LangException($"Function 'round' expects a number argument for digits, but got '{GetValueType(digits)}'", GetCallToken(call).Line, _filePath);
        }

        return Math.Round(d, (int)digitsDouble);
    }

    private object? CallInternalFunctionFloor(Expression.Call call)
    {
        CheckNumberOfArguments(call, 1, "floor");

        object? number = Evaluate(call.Arguments[0]);
        if (number is not double d)
        {
            throw new LangException($"Function 'floor' expects a number argument, but got '{GetValueType(number)}'", GetCallToken(call).Line, _filePath);
        }

        return Math.Floor(d);
    }

    private object? CallInternalFunctionCeil(Expression.Call call)
    {
        CheckNumberOfArguments(call, 1, "ceil");

        object? number = Evaluate(call.Arguments[0]);
        if (number is not double d)
        {
            throw new LangException($"Function 'ceil' expects a number argument, but got '{GetValueType(number)}'", GetCallToken(call).Line, _filePath);
        }

        return Math.Ceiling(d);
    }

    private object? CallInternalFunctionCharCode(Expression.Call call)
    {
        CheckNumberOfArguments(call, 1, "charCode");

        object? argValue = Evaluate(call.Arguments[0]);
        if (argValue is not string str)
        {
            throw new LangException($"Function 'charCode' expects a string argument, but got '{GetValueType(argValue)}'", GetCallToken(call).Line, _filePath);
        }
        if (str.Length != 1)
        {
            throw new LangException($"Function 'charCode' expects a single-character string, but got '{str}'", GetCallToken(call).Line, _filePath);
        }

        return (double)str[0];
    }

    private object? CallInternalFunctionCharFromCode(Expression.Call call)
    {
        CheckNumberOfArguments(call, 1, "charFromCode");

        object? argValue = Evaluate(call.Arguments[0]);
        if (argValue is not double codeDouble)
        {
            throw new LangException($"Function 'charFromCode' expects a number argument, but got '{GetValueType(argValue)}'", GetCallToken(call).Line, _filePath);
        }

        int code = (int)codeDouble;
        if (code < 0 || code > 0x10FFFF || (code >= 0xD800 && code <= 0xDFFF))
        {
            throw new LangException($"Function 'charFromCode' expects a valid Unicode code point, but got {code}", GetCallToken(call).Line, _filePath);
        }

        return new string(char.ConvertFromUtf32(code));
    }

    private object? CallInternalFunctionCharToUpper(Expression.Call call)
    {
        CheckNumberOfArguments(call, 1, "charToUpper");

        object? argValue = Evaluate(call.Arguments[0]);
        if (argValue is not string str)
        {
            throw new LangException($"Function 'charToUpper' expects a string argument, but got '{GetValueType(argValue)}'", GetCallToken(call).Line, _filePath);
        }
        if (str.Length != 1)
        {
            throw new LangException($"Function 'charToUpper' expects a single-character string, but got '{str}'", GetCallToken(call).Line, _filePath);
        }

        return char.ToUpper(str[0]).ToString();
    }

    private object? CallInternalFunctionCharToLower(Expression.Call call)
    {
        CheckNumberOfArguments(call, 1, "charToLower");

        object? argValue = Evaluate(call.Arguments[0]);
        if (argValue is not string str)
        {
            throw new LangException($"Function 'charToLower' expects a string argument, but got '{GetValueType(argValue)}'", GetCallToken(call).Line, _filePath);
        }
        if (str.Length != 1)
        {
            throw new LangException($"Function 'charToLower' expects a single-character string, but got '{str}'", GetCallToken(call).Line, _filePath);
        }

        return char.ToLower(str[0]).ToString();
    }

    private object? CallInternalFunctionCharCount(Expression.Call call)
    {
        CheckNumberOfArguments(call, 1, "charCount");

        object? argValue = Evaluate(call.Arguments[0]);
        if (argValue is not string str)
        {
            throw new LangException($"Function 'charCount' expects a string argument, but got '{GetValueType(argValue)}'", GetCallToken(call).Line, _filePath);
        }

        return (double)str.EnumerateRunes().Count();
    }

    private object? CallInternalFunctionCharAt(Expression.Call call)
    {
        CheckNumberOfArguments(call, 2, "charAt");

        object? strValue = Evaluate(call.Arguments[0]);
        if (strValue is not string str)
        {
            throw new LangException($"Function 'charAt' expects first argument to be a string, but got '{GetValueType(strValue)}'", GetCallToken(call).Line, _filePath);
        }

        object? indexValue = Evaluate(call.Arguments[1]);
        if (indexValue is not double indexDouble)
        {
            throw new LangException($"Function 'charAt' expects second argument to be a number, but got '{GetValueType(indexValue)}'", GetCallToken(call).Line, _filePath);
        }

        int index = (int)indexDouble;
        var runes = str.EnumerateRunes().ToList();

        if (index < 0 || index >= runes.Count)
        {
            throw new LangException($"Function 'charAt' index {index} is out of bounds for string of length {runes.Count}", GetCallToken(call).Line, _filePath);
        }

        return runes[index].ToString();
    }

    private object? CallInternalFunctionClock(Expression.Call call)
    {
        CheckNumberOfArguments(call, 0, "clock");

        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
    }

    private object? CallInternalFunctionExit(Expression.Call call)
    {
        CheckNumberOfArguments(call, 1, "exit");

        object? argValue = Evaluate(call.Arguments[0]);
        if (argValue is not double d)
        {
            throw new LangException($"Function 'exit' expects an integer argument, but got '{GetValueType(argValue)}'", GetCallToken(call).Line, _filePath);
        }

        Environment.Exit((int)d);

        // this line will never be reached
        return null;
    }

    private object? CallInternalFunctionAssert(Expression.Call call)
    {
        CheckNumberOfArguments(call, 1, "assert");

        object? argValue = Evaluate(call.Arguments[0]);
        if (argValue is not bool b)
        {
            throw new LangException($"Function 'assert' expects a condition, but got '{GetValueType(argValue)}'", GetCallToken(call).Line, _filePath);
        }

        if (b == false)
        {
            throw new LangException($"Assertion failed", GetCallToken(call).Line, _filePath);
        }

        return null;
    }

    private object? CallInternalFunctionSleepFor(Expression.Call call)
    {
        CheckNumberOfArguments(call, 1, "sleepFor");

        object? argValue = Evaluate(call.Arguments[0]);
        if (argValue is not double d)
        {
            throw new LangException($"Function 'sleepFor' expects a number argument, but got '{GetValueType(argValue)}'", GetCallToken(call).Line, _filePath);
        }

        int milliseconds = (int)(d * 1000);
        Thread.Sleep(milliseconds);

        return null;
    }

    private object? CallInternalFunctionToNumber(Expression.Call call)
    {
        CheckNumberOfArguments(call, 1, "toNumber");

        object? argValue = Evaluate(call.Arguments[0]);

        return convertToNumber(argValue, "toNumber", call);
    }

    private object? CallInternalFunctionToString(Expression.Call call)
    {
        CheckNumberOfArguments(call, 1, "toString");

        object? argValue = Evaluate(call.Arguments[0]);

        return Stringify(argValue);
    }
}
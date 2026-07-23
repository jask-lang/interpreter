namespace JaskLang;

public partial class Interpreter
{
    public delegate object? InternalFunctionDelegate(Expression.Call call);

    // dictionary for internal functions: name -> delegate
    private readonly Dictionary<string, InternalFunctionDelegate> _internalFunctions = [];

    private void initInternalFunctions()
    {
        // standard functions
        _internalFunctions["print"]       = CallInternalFunctionPrint;
        _internalFunctions["printLine"]   = CallInternalFunctionPrintLine;
        _internalFunctions["type"]        = CallInternalFunctionType;
        _internalFunctions["clock"]       = CallInternalFunctionClock;
        _internalFunctions["exit"]        = CallInternalFunctionExit;
        _internalFunctions["assert"]      = CallInternalFunctionAssert;
        _internalFunctions["sleepFor"]    = CallInternalFunctionSleepFor;

        // variable convertions
        _internalFunctions["toNumber"]    = CallInternalFunctionToNumber;
        _internalFunctions["toString"]    = CallInternalFunctionToString;

        // math functions
        _internalFunctions["round"]       = CallInternalFunctionRound;
        _internalFunctions["floor"]       = CallInternalFunctionFloor;
        _internalFunctions["ceil"]        = CallInternalFunctionCeil;

        // string functions
        _internalFunctions["stringGetIndexOf"]   = CallInternalFunctionStringGetIndexOf;
        _internalFunctions["stringGetSubstring"] = CallInternalFunctionStringGetSubstring;

        // list functions
        initInternalFunctionsList();

        // trust engine
        initInternalFunctionsTrustEngine();

        // IO functions
        _internalFunctions["readInput"]  = CallInternalFunctionReadInput;
        _internalFunctions["readFile"]   = CallInternalFunctionReadFile;
        _internalFunctions["writeFile"]  = CallInternalFunctionWriteFile;
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

    private object? CallInternalFunctionStringGetIndexOf(Expression.Call call)
    {
        CheckNumberOfArguments(call, 2, "stringGetIndexOf");

        object? strValue = Evaluate(call.Arguments[0]);
        if (strValue is not string str)
        {
            throw new LangException($"Function 'stringGetIndexOf' expects a string argument, but got '{GetValueType(strValue)}'", GetCallToken(call).Line, _filePath);
        }

        object? searchValue = Evaluate(call.Arguments[1]);
        if (searchValue is not string search)
        {
            throw new LangException($"Function 'stringGetIndexOf' expects a string argument for search, but got '{GetValueType(searchValue)}'", GetCallToken(call).Line, _filePath);
        }

        return (double)str.IndexOf(search);
    }

    private object? CallInternalFunctionStringGetSubstring(Expression.Call call)
    {
        CheckNumberOfArguments(call, 3, "stringGetSubstring");

        object? strValue = Evaluate(call.Arguments[0]);
        if (strValue is not string str)
        {
            throw new LangException($"Function 'stringGetSubstring' expects a string argument, but got '{GetValueType(strValue)}'", GetCallToken(call).Line, _filePath);
        }

        object? startIndexValue = Evaluate(call.Arguments[1]);
        if (startIndexValue is not double startIndexDouble)
        {
            throw new LangException($"Function 'stringGetSubstring' expects a number argument for start index, but got '{GetValueType(startIndexValue)}'", GetCallToken(call).Line, _filePath);
        }
        int startIndex = (int)startIndexDouble;

        object? lengthValue = Evaluate(call.Arguments[2]);
        if (lengthValue is not double lengthDouble)
        {
            throw new LangException($"Function 'stringGetSubstring' expects a number argument for length, but got '{GetValueType(lengthValue)}'", GetCallToken(call).Line, _filePath);
        }
        int length = (int)lengthDouble;

        return str.Substring(startIndex, length);
    }

    private object? CallInternalFunctionClock(Expression.Call call)
    {
        CheckNumberOfArguments(call, 0, "clock");

        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
    }

    private object? CallInternalFunctionReadInput(Expression.Call call)
    {
        if (_permissionManager.IsPermitted(Permission.Stdin) == false)
        {
            throw new LangException($"Missing permission 'stdin' for function 'readInput'", GetCallToken(call).Line, _filePath);
        }

        if (call.Arguments.Count > 1)
        {
            throw new LangException($"Function 'readInput' expects 0 or 1 argument, but got {call.Arguments.Count}", GetCallToken(call).Line, _filePath);
        }

        // if there's one argument, print it as a prompt
        if (call.Arguments.Count == 1)
        {
            object? promptValue = Evaluate(call.Arguments[0]);
            Console.Write(Stringify(promptValue));
        }

        return new UntrustedValue(Console.ReadLine() ?? null);
    }

    private object? CallInternalFunctionReadFile(Expression.Call call)
    {
        if (_permissionManager.IsPermitted(Permission.FileRead) == false)
        {
            throw new LangException($"Missing permission 'read' for function 'readFile'", GetCallToken(call).Line, _filePath);
        }

        CheckNumberOfArguments(call, 1, "readFile");

        object? pathArg = Evaluate(call.Arguments[0]);
        if (pathArg is not string path)
        {
            throw new LangException($"Function 'readFile' expects a string argument, but got '{GetValueType(pathArg)}'", GetCallToken(call).Line, _filePath);
        }

        if (_permissionManager.IsPathPermitted(Permission.FileRead, path) == false)
        {
            throw new LangException($"Missing permission 'read' on '{path}' for function 'readFile'", GetCallToken(call).Line, _filePath);
        }

        if (File.Exists(path) == false)
        {
            throw new LangException($"File at path '{path}' cannot be found", GetCallToken(call).Line, _filePath);
        }

        try
        {
            string content = File.ReadAllText(path);
            return new UntrustedValue(content);
        }
        catch
        {
            throw new LangException($"Reading file at '{path}' failed", GetCallToken(call).Line, _filePath);
        }
    }

    private object? CallInternalFunctionWriteFile(Expression.Call call)
    {
        if (_permissionManager.IsPermitted(Permission.FileWrite) == false)
        {
            throw new LangException($"Missing permission 'write' for function 'writeFile'", GetCallToken(call).Line, _filePath);
        }

        CheckNumberOfArguments(call, 2, "writeFile");

        object? pathArg = Evaluate(call.Arguments[0]);
        if (pathArg is not string path)
        {
            throw new LangException($"Function 'writeFile' expects a string argument for path, but got '{GetValueType(pathArg)}'", GetCallToken(call).Line, _filePath);
        }

        if (_permissionManager.IsPathPermitted(Permission.FileWrite, path) == false)
        {
            throw new LangException($"Missing permission 'write' on '{path}' for function 'writeFile'", GetCallToken(call).Line, _filePath);
        }

        object? contentArg = Evaluate(call.Arguments[1]);
        if (contentArg is not string content)
        {
            throw new LangException($"Function 'writeFile' expects a string argument for content, but got '{GetValueType(contentArg)}'", GetCallToken(call).Line, _filePath);
        }

        try
        {
            File.WriteAllText(path, content);
        }
        catch
        {
            throw new LangException($"Writing file at '{path}' failed", GetCallToken(call).Line, _filePath);
        }

        return null;
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
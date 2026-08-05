namespace JaskLang;

public partial class Interpreter
{
    private void initInternalFunctionsIO()
    {
        _internalFunctions["readInput"]  = CallInternalFunctionReadInput;
        _internalFunctions["readFile"]   = CallInternalFunctionReadFile;
        _internalFunctions["writeFile"]  = CallInternalFunctionWriteFile;
        _internalFunctions["fileExists"] = CallInternalFunctionFileExists;
    }

    /// <summary>
    /// Reads from stdio, optionally printing a prompt first
    /// </summary>
    /// <param name="call"></param>
    /// <returns>A Result struct containing an untrusted value</returns>
    /// <exception cref="LangException">Throws on missing permissions or wrong type of param</exception>
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

        try
        {
            string input = Console.ReadLine() ?? "";
            return createStructInstanceFromResult(ResultType.OK, new UntrustedValue(input), null);
        }
        catch
        {
            return createStructInstanceFromResult(ResultType.NOT_OK, null, "Reading input failed");
        }
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
            return createStructInstanceFromResult(ResultType.OK, new UntrustedValue(content), null);
        }
        catch
        {
            return createStructInstanceFromResult(ResultType.NOT_OK, null, $"Reading file at '{path}' failed");
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
            return createStructInstanceFromResult(ResultType.OK, null, null);
        }
        catch
        {
            return createStructInstanceFromResult(ResultType.NOT_OK, null, $"Writing file at '{path}' failed");
        }
    }

    private object? CallInternalFunctionFileExists(Expression.Call call)
    {
        if (_permissionManager.IsPermitted(Permission.FileRead) == false)
        {
            throw new LangException($"Missing permission 'read' for function 'fileExists'", GetCallToken(call).Line, _filePath);
        }

        CheckNumberOfArguments(call, 1, "fileExists");

        object? pathArg = Evaluate(call.Arguments[0]);
        if (pathArg is not string path)
        {
            throw new LangException($"Function 'fileExists' expects a string argument, but got '{GetValueType(pathArg)}'", GetCallToken(call).Line, _filePath);
        }

        if (_permissionManager.IsPathPermitted(Permission.FileRead, path) == false)
        {
            throw new LangException($"Missing permission 'read' on '{path}' for function 'fileExists'", GetCallToken(call).Line, _filePath);
        }

        return File.Exists(path);
    }
}
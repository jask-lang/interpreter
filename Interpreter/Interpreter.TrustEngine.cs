namespace JaskLang;

public class UntrustedValue : object
{
    public object? Value { get; set; }
    public UntrustedValue(object? value)
    {
        Value = value;
    }
}

public partial class Interpreter
{
    private void initInternalFunctionsTrustEngine()
    {
        _internalFunctions["trust"]     = CallInternalFunctionTrust;
        _internalFunctions["verify"]    = CallInternalFunctionVerify;
        _internalFunctions["untrusted"] = CallInternalFunctionUntrusted;
    }

    private object CallInternalFunctionTrust(Expression.Call call)
    {
        if (_permissionManager.IsPermitted(Permission.Trust) == false)
        {
            throw new LangException("Missing permission 'allow-trust-override' for function 'trust'", GetCallToken(call).Line, _filePath);
        }

        CheckNumberOfArguments(call, 1, "trust");

        object? untrustedValueObj = Evaluate(call.Arguments[0]);
        if (untrustedValueObj is not UntrustedValue uv)
        {
            throw new LangException($"Function 'trust' expects an untrusted value but got '{GetValueType(untrustedValueObj)}'", GetCallToken(call).Line, _filePath);
        }

        if (uv.Value != null)
        {
            return uv.Value;
        }

        throw new LangException("Trusting untrusted value failed", GetCallToken(call).Line, _filePath);
    }

    private object CallInternalFunctionVerify(Expression.Call call)
    {
        CheckNumberOfArguments(call, 2, "verify");

        object? untrustedValueObj = Evaluate(call.Arguments[0]);
        if (untrustedValueObj is not UntrustedValue uv)
        {
            throw new LangException($"Function 'verify' expects an untrusted value but got '{GetValueType(untrustedValueObj)}'", GetCallToken(call).Line, _filePath);
        }

        object? patternObj = Evaluate(call.Arguments[1]);
        if (patternObj is not string pattern)
        {
            throw new LangException($"Function 'verify' expects a string pattern but got '{GetValueType(patternObj)}'", GetCallToken(call).Line, _filePath);
        }

        switch (pattern)
        {
            case "string":
                return Stringify(uv.Value);
            
            case "number":
                return convertToNumber(uv.Value, "verify", call);
        }

        if (uv.Value is string s)
        {
            if (pattern == "boolean")
            {
                if (s == "true")  return true;
                if (s == "false") return false;
            }
        }

        throw new LangException($"Verify failed for untrusted value '{uv.Value}' and pattern '{pattern}'", GetCallToken(call).Line, _filePath);
    }

    private object CallInternalFunctionUntrusted(Expression.Call call)
    {
        CheckNumberOfArguments(call, 1, "untrusted");

        object? valueObj = Evaluate(call.Arguments[0]);
        if (valueObj is UntrustedValue)
        {
            throw new LangException($"Function 'untrusted' expects a value but got '{GetValueType(valueObj)}'", GetCallToken(call).Line, _filePath);
        }

        return new UntrustedValue(valueObj);
    }
}
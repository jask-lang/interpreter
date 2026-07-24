namespace JaskLang;

public enum ResultType : uint
{
    OK = 0,
    NOT_OK = 1
}

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
        _internalFunctions["untrust"] = CallInternalFunctionUntrust;
    }

    private object? CallInternalFunctionTrust(Expression.Call call)
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

        return uv.Value;
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

        if (uv.Value == null)
        {
            return createStructInstanceFromResult(ResultType.NOT_OK, null, "Untrusted value for verify is nil");
        }

        switch (pattern)
        {
            case "string":
                return createStructInstanceFromResult(ResultType.OK, Stringify(uv.Value));
            
            case "number":
                if (double.TryParse(uv.Value.ToString(), out double num))
                {
                    return createStructInstanceFromResult(ResultType.OK, num);
                }
                return createStructInstanceFromResult(ResultType.NOT_OK, null, $"Cannot verify value '{uv.Value}' with pattern '{pattern}'");
            
            case "boolean":
                if (uv.Value.ToString() == "true")  return createStructInstanceFromResult(ResultType.OK, true);
                if (uv.Value.ToString() == "false") return createStructInstanceFromResult(ResultType.OK, false);
                break;
        }

        return createStructInstanceFromResult(ResultType.NOT_OK, null, $"Unknown verify pattern '{pattern}' for value {uv.Value}");
    }

    private object CallInternalFunctionUntrust(Expression.Call call)
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
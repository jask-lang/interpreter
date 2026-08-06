using System.Text.Json;

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
        _internalFunctions["trust"]   = CallInternalFunctionTrust;
        _internalFunctions["verify"]  = CallInternalFunctionVerify;
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
                if (uv.Value.ToString() == "true"  || uv.Value.ToString() == "1")  return createStructInstanceFromResult(ResultType.OK, true);
                if (uv.Value.ToString() == "false" || uv.Value.ToString() == "0") return createStructInstanceFromResult(ResultType.OK, false);
                break;

            case "json":
                if (uv.Value is not string jsonText)
                {
                    return createStructInstanceFromResult(ResultType.NOT_OK, null, $"Cannot verify value '{uv.Value}' with pattern '{pattern}'");
                }

                try
                {
                    using JsonDocument document = JsonDocument.Parse(jsonText);
                    object parsedValue = ConvertJsonElement(document.RootElement);
                    return createStructInstanceFromResult(ResultType.OK, parsedValue);
                }
                catch (JsonException)
                {
                    return createStructInstanceFromResult(ResultType.NOT_OK, null, $"Cannot verify value '{uv.Value}' with pattern '{pattern}'");
                }
        }

        return createStructInstanceFromResult(ResultType.NOT_OK, null, $"Unknown verify pattern '{pattern}' for value {uv.Value}");
    }

    private object ConvertJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => ConvertJsonObject(element),
            JsonValueKind.Array => ConvertJsonArray(element),
            JsonValueKind.String => element.GetString()!,
            JsonValueKind.Number when element.TryGetInt64(out long intValue) => intValue,
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null!,
            _ => null!
        };
    }

    private Dictionary<object, object> ConvertJsonObject(JsonElement element)
    {
        var map = new Dictionary<object, object>(element.EnumerateObject().Count());
        foreach (var property in element.EnumerateObject())
        {
            map[property.Name] = ConvertJsonElement(property.Value) ?? null!;
        }

        return map;
    }

    private List<object?> ConvertJsonArray(JsonElement element)
    {
        var list = new List<object?>(element.GetArrayLength());
        foreach (var item in element.EnumerateArray())
        {
            list.Add(ConvertJsonElement(item));
        }

        return list;
    }

    private object CallInternalFunctionUntrust(Expression.Call call)
    {
        CheckNumberOfArguments(call, 1, "untrust");

        object? valueObj = Evaluate(call.Arguments[0]);
        if (valueObj is UntrustedValue)
        {
            throw new LangException($"Function 'untrust' expects a value but got '{GetValueType(valueObj)}'", GetCallToken(call).Line, _filePath);
        }

        return new UntrustedValue(valueObj);
    }
}
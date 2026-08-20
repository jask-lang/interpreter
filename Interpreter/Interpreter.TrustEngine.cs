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
        RegisterInternalFunction("trust",   new List<(string, string)> { ("untrustedValue", "untrusted") },                        CallInternalFunctionTrust);
        RegisterInternalFunction("verify",  new List<(string, string)> { ("untrustedValue", "untrusted"), ("pattern", "string") }, CallInternalFunctionVerify);
        RegisterInternalFunction("untrust", new List<(string, string)> { ("value", "any") },                                       CallInternalFunctionUntrust);
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

        // we can safely say that this is not null...
        string rawValue = uv.Value.ToString()!.Trim();

        switch (pattern)
        {
            case "string":
                return createStructInstanceFromResult(ResultType.OK, Stringify(uv.Value));

            case "number":
                if (double.TryParse(rawValue, out double num))
                {
                    return createStructInstanceFromResult(ResultType.OK, num);
                }
                break;

            case "boolean":
                if (rawValue == "true"  || rawValue == "1") return createStructInstanceFromResult(ResultType.OK, true);
                if (rawValue == "false" || rawValue == "0") return createStructInstanceFromResult(ResultType.OK, false);
                break;

            case "json":
                if (rawValue is string jsonText)
                {
                    try
                    {
                        using JsonDocument document = JsonDocument.Parse(jsonText);
                        object parsedValue = ConvertJsonElement(document.RootElement);
                        return createStructInstanceFromResult(ResultType.OK, parsedValue);
                    }
                    catch (Exception) { }
                }
                break;
        }

        return createStructInstanceFromResult(ResultType.NOT_OK, null, $"Cannot verify value '{uv.Value}' for pattern '{pattern}'");
    }

    private object ConvertJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => ConvertJsonObject(element),
            JsonValueKind.Array => ConvertJsonArray(element),
            JsonValueKind.String => element.GetString()!,
            JsonValueKind.Number when element.TryGetInt64(out long intValue) => (double)intValue,
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
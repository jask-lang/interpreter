namespace JaskLang;

public partial class Interpreter
{
    private static readonly HttpClient httpClient = new HttpClient();

    private void initInternalFunctionsHttp()
    {
        _internalFunctions["httpGet"] = CallInternalFunctionHttpGet;
    }

    private StructInstance CreateStructHTTPResult(Dictionary<object, object> responseHeaders, string content, double statusCode)
    {
        var httpResponseFields = new Dictionary<string, object?>
        {
            { "statusCode", statusCode },
            { "body", new UntrustedValue(content) },
            { "headers", responseHeaders }
        };

        return new StructInstance("HttpResponse", httpResponseFields);
    }

    private object? CallInternalFunctionHttpGet(Expression.Call call)
    {
        if (_permissionManager.IsPermitted(Permission.Network) == false)
        {
            throw new LangException("Missing permission 'allow-network' for function 'httpGet'", GetCallToken(call).Line, _filePath);
        }

        object? urlObj = Evaluate(call.Arguments[0]);
        if (urlObj is not string url)
        {
            throw new LangException($"Function 'httpGet' expects a string but got '{GetValueType(urlObj)}'", GetCallToken(call).Line, _filePath);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, url);

        // second argument is optional and should contain a map with headers
        if (call.Arguments.Count == 2)
        {
            object? authMapObj = Evaluate(call.Arguments[1]);

            if (authMapObj is not Dictionary<object, object> authMap)
            {
                throw new LangException($"Function 'httpGet' expects a map, but got '{GetValueType(authMapObj)}'", GetCallToken(call).Line, _filePath);
            }

            foreach (var kvp in authMap)
            {
                if (kvp.Key is not string key)
                {
                    throw new LangException($"Function 'httpGet' expects a map with string keys, but got key of type '{GetValueType(kvp.Key)}'", GetCallToken(call).Line, _filePath);
                }

                if (kvp.Value is not string value)
                {
                    throw new LangException($"Function 'httpGet' expects a map with string values, but got value of type '{GetValueType(kvp.Value)}'", GetCallToken(call).Line, _filePath);
                }

                request.Headers.TryAddWithoutValidation(key, value);
            }
        }

        try
        {
            using var response = httpClient.SendAsync(request).GetAwaiter().GetResult();
            string content = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

            var responseHeaders = new Dictionary<object, object>();
            foreach (var header in response.Headers)
            {
                responseHeaders[header.Key] = string.Join(", ", header.Value);
            }
            if (response.Content != null)
            {
                foreach (var header in response.Content.Headers)
                {
                    responseHeaders[header.Key] = string.Join(", ", header.Value);
                }
            }

            return createStructInstanceFromResult(ResultType.OK, CreateStructHTTPResult(responseHeaders, content, (double)response.StatusCode));
        }
        catch (Exception ex)
        {
            return createStructInstanceFromResult(ResultType.NOT_OK, null, $"Function 'httpGet' failed to fetch URL '{url}': {ex.Message}");
        }
    }
}
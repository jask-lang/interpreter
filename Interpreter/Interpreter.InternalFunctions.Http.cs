namespace JaskLang;

public partial class Interpreter
{
    private static readonly HttpClient httpClient = new HttpClient();

    private void initInternalFunctionsHttp()
    {
        RegisterInternalFunction("get",  new List<(string, string)> { ("url", "string"), ("headers", "map") },                     CallInternalFunctionHttpGet);
        RegisterInternalFunction("post", new List<(string, string)> { ("url", "string"), ("headers", "map"), ("body", "string") }, CallInternalFunctionHttpPost);
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

    private StructInstance sendHTTPRequest(string function, Expression.Call call)
    {
        object? urlObj = Evaluate(call.Arguments[0]);
        if (urlObj is not string url)
        {
            throw new LangException($"Function '{function}' expects a string but got '{GetValueType(urlObj)}'", GetCallToken(call).Line, _filePath);
        }

        object? authMapObj = Evaluate(call.Arguments[1]);

        if (authMapObj is not Dictionary<object, object> authMap)
        {
            throw new LangException($"Function '{function}' expects a map, but got '{GetValueType(authMapObj)}'", GetCallToken(call).Line, _filePath);
        }

        var method = function == "post" ? HttpMethod.Post : HttpMethod.Get;

        using var request = new HttpRequestMessage(method, url);

        foreach (var kvp in authMap)
        {
            if (kvp.Value is not string value)
            {
                throw new LangException($"Function '{function}' expects a map with string values, but got value of type '{GetValueType(kvp.Value)}'", GetCallToken(call).Line, _filePath);
            }

            request.Headers.TryAddWithoutValidation((string)kvp.Key, value);
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
            return createStructInstanceFromResult(ResultType.NOT_OK, null, $"Function '{function}' failed to fetch '{request.RequestUri}': {ex.Message}");
        }
    }

    private object? CallInternalFunctionHttpGet(Expression.Call call)
    {
        if (_permissionManager.IsPermitted(Permission.Network) == false)
        {
            throw new LangException("Missing permission 'allow-network' for function 'get'", GetCallToken(call).Line, _filePath);
        }

        return sendHTTPRequest("get", call);
    }

    private object? CallInternalFunctionHttpPost(Expression.Call call)
    {
        if (_permissionManager.IsPermitted(Permission.Network) == false)
        {
            throw new LangException("Missing permission 'allow-network' for function 'post'", GetCallToken(call).Line, _filePath);
        }

        return sendHTTPRequest("post", call);
    }
}
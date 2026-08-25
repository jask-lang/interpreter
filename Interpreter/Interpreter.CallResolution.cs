namespace JaskLang;

// precomputed metadata for one user defined function overload, built once at
// definition time so that call resolution never has to re-scan the raw
// parameter token lists on every invocation
public sealed class UserFunctionInfo
{
    public readonly List<(Token Name, Token Type, JaskLang.Expression? Default)> Params;
    public readonly List<Statement> Body;
    public readonly int ParamCount;
    public readonly int RequiredCount;
    public readonly int AnyCount;
    public readonly bool[] IsDefault;
    public readonly bool[] IsAny;
    public readonly string[] TypeNames;
    public readonly HashSet<string> ParamNameSet;

    public UserFunctionInfo(List<(Token Name, Token Type, JaskLang.Expression? Default)> parameters, List<Statement> body)
    {
        Params = parameters;
        Body = body;
        ParamCount = parameters.Count;
        IsDefault = new bool[ParamCount];
        IsAny = new bool[ParamCount];
        TypeNames = new string[ParamCount];
        ParamNameSet = new HashSet<string>(ParamCount, StringComparer.Ordinal);

        for (int i = 0; i < ParamCount; i++)
        {
            bool isDefault = parameters[i].Item3 != null;
            IsDefault[i] = isDefault;
            if (isDefault == false)
            {
                RequiredCount++;
            }

            string typeName = parameters[i].Type.Lexeme;
            TypeNames[i] = typeName;
            if (typeName == "any")
            {
                AnyCount++;
            }

            ParamNameSet.Add(parameters[i].Name.Lexeme);
        }
    }
}

// the result of resolving a call site against the interpreters function table.
// cached on the AST node and invalidated through _resolutionGeneration, which
// is bumped whenever a new function, struct or internal function group is defined
public sealed class CallResolution
{
    public Interpreter.InternalFunctionDelegate? Internal { get; }
    public bool HasStruct { get; }
    public Dictionary<string, object?>? StructDefaults { get; }
    public bool HasUserOverloads { get; }
    public UserFunctionInfo? Single { get; }
    public UserFunctionInfo[]? Candidates { get; }

    public bool IsInternalOnly => HasUserOverloads == false && Internal != null;

    public CallResolution(
        Interpreter.InternalFunctionDelegate? internalFunc,
        bool hasStruct,
        Dictionary<string, object?>? structDefaults,
        List<UserFunctionInfo>? overloads)
    {
        Internal = internalFunc;
        HasStruct = hasStruct;
        StructDefaults = structDefaults;
        HasUserOverloads = overloads != null && overloads.Count > 0;

        if (HasUserOverloads)
        {
            Candidates = overloads!.Count == 1 ? [overloads[0]] : overloads.ToArray();
            Single = overloads.Count == 1 ? overloads[0] : null;
        }
    }
}

public partial class Interpreter
{
    // caches of the named parameter metadata for internal functions, built lazily once per function name
    private readonly Dictionary<string, (string[] Names, HashSet<string> Set)> _internalFunctionParamNameCache = new(StringComparer.OrdinalIgnoreCase);

    private (string[] Names, HashSet<string> Set) GetInternalFunctionParamNamesCached(string funcName)
    {
        if (_internalFunctionParamNameCache.TryGetValue(funcName, out var cached))
        {
            return cached;
        }

        if (_internalFunctionParamNames.TryGetValue(funcName, out var paramNames))
        {
            string[] names = new string[paramNames.Count];
            var set = new HashSet<string>(names.Length, StringComparer.Ordinal);

            for (int i = 0; i < names.Length; i++)
            {
                names[i] = paramNames[i].Name;
                set.Add(names[i]);
            }

            cached = (names, set);
            _internalFunctionParamNameCache[funcName] = cached;
            return cached;
        }

        throw new LangException($"Function '{funcName}' does not support named parameters", new Token(TokenType.Identifier, funcName, null, 0).Line, _filePath);
    }

    // resolves a call site against the current function table; the result is
    // meant to be cached on the AST node until _resolutionGeneration changes
    private CallResolution ResolveCall(string name)
    {
        _functionOverloads.TryGetValue(name, out var overloads);
        _internalFunctions.TryGetValue(name, out var internalFunc);
        bool hasStruct = _structs.TryGetValue(name, out var structDefaults);

        return new CallResolution(internalFunc, hasStruct, structDefaults, overloads);
    }
}

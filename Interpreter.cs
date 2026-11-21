namespace JaskLang;

public sealed class ReturnException : Exception
{
    public object? Value { get; }
    public ReturnException(object? value) : base() { Value = value; }
    public override string StackTrace => string.Empty;
}

public sealed class BreakException : Exception
{
    public BreakException() : base() { }
    public override string StackTrace => string.Empty;
}

public sealed class ContinueException : Exception
{
    public ContinueException() : base() { }
    public override string StackTrace => string.Empty;
}

public class RestrictedValue : object
{
    public object Value { get; set; }
    public RestrictedValue(object value)
    {
        Value = value;
    }
}

public partial class Interpreter
{
    // dictionary for functions: "name(type1,type2,...)" -> (parameters, body)
    private readonly Dictionary<string, (List<(Token Name, Token Type, JaskLang.Expression? Default)> Params, List<Statement> Body)> _functions = new(StringComparer.Ordinal);

    // direct lookup for user-defined overloads by function name, avoiding a full scan on every call
    private readonly Dictionary<string, List<UserFunctionInfo>> _functionOverloads = new(StringComparer.Ordinal);

    // bumped whenever a new function, struct or internal function group is defined;
    // used to invalidate the per-call-site resolutions cached on the AST nodes
    private int _resolutionGeneration;

    // dictionary for struct definitions: name -> pre-evaluated default field values
    private readonly Dictionary<string, Dictionary<string, object?>> _structs = new(StringComparer.Ordinal);

    // exported function names (by overload lookup key) - only these are callable from other modules
    private readonly HashSet<string> _exportedFunctionNames = new(StringComparer.Ordinal);

    // exported struct names - only these are instantiable from other modules
    private readonly HashSet<string> _exportedStructNames = new(StringComparer.Ordinal);

    // dictionary for imported modules: alias -> isolated interpreter instance running that module
    private readonly Dictionary<string, Interpreter> _modules = new(StringComparer.Ordinal);

    // tracks module file paths currently being loaded (by full path), to detect circular 'use' chains
    private readonly HashSet<string> _modulesLoading;

    // base directory used to resolve relative module paths (directory of the current script)
    private readonly string _baseDirectory;

    // initial process directory used as fallback for module resolution
    private readonly string _processDirectory;

    // current file path for error reporting
    private readonly string? _filePath;

    private readonly List<Dictionary<string, object?>> _scopes = new();

    // pool of reusable function-scope dictionaries to avoid per-call GC pressure
    private readonly Stack<Dictionary<string, object?>> _functionScopePool;

    // cached global environment reference; always at index 0
    private Dictionary<string, object?> _globalEnvironment;

    private Dictionary<string, object?> CurrentEnvironment => _scopes[_scopes.Count - 1];

    // return value for the current function call
    private object? _returnValue;
    private bool _returning;

    private PermissionManager _permissionManager;

    private bool _isInteractiveMode;

    public Interpreter(PermissionManager permissionManager) : this(new HashSet<string>(), Directory.GetCurrentDirectory(), Directory.GetCurrentDirectory(), null, permissionManager, isInteractiveMode: false) { }

    public Interpreter(string baseDirectory, string? filePath, PermissionManager permissionManager) : this(new HashSet<string>(), baseDirectory, Directory.GetCurrentDirectory(), filePath, permissionManager, isInteractiveMode: false) { }

    public Interpreter(PermissionManager permissionManager, bool isInteractiveMode) : this(new HashSet<string>(), Directory.GetCurrentDirectory(), Directory.GetCurrentDirectory(), null, permissionManager, isInteractiveMode) { }

    // internal constructor used when loading a module, so the circular-import guard is shared across the whole chain
    private Interpreter(HashSet<string> modulesLoading, string baseDirectory, string processDirectory, string? filePath, PermissionManager permissionManager, bool isInteractiveMode = false)
    {
        _modulesLoading = modulesLoading;
        _baseDirectory = baseDirectory;
        _processDirectory = processDirectory;
        _filePath = filePath;
        _globalEnvironment = new Dictionary<string, object?>(StringComparer.Ordinal);
        _scopes.Add(_globalEnvironment);
        _functionScopePool = new();
        _permissionManager = permissionManager;
        _isInteractiveMode = isInteractiveMode;
        initInternalFunctions();
    }

    private Dictionary<string, object?> RentFunctionScope()
    {
        if (_functionScopePool.TryPop(out var dict))
        {
            return dict;
        }
        return new Dictionary<string, object?>(StringComparer.Ordinal);
    }

    private void ReturnFunctionScope(Dictionary<string, object?> dict)
    {
        dict.Clear();
        _functionScopePool.Push(dict);
    }

    public void Interpret(List<Statement> statements)
    {
        foreach (var statement in statements)
        {
            Execute(statement);
        }
    }

    public object? InterpretSource(string source)
    {
        _scopes.Add(new Dictionary<string, object?>(StringComparer.Ordinal));

        try
        {
            var lexer = new Lexer(source, false, _filePath);
            var tokens = lexer.ScanTokens();
            var parser = new Parser(tokens, _filePath);
            var statements = parser.Parse();

            object? lastResult = null;
            foreach (var statement in statements)
            {
                if (statement is Statement.Expression expr)
                {
                    lastResult = Evaluate(expr.Value);
                }
                else
                {
                    Execute(statement);
                }
            }

            return lastResult;
        }
        finally
        {
            _scopes.RemoveAt(_scopes.Count - 1);
        }
    }

    private void Execute(Statement statement)
    {
        switch (statement)
        {
            case Statement.Set s:
                var variableName = s.Name.Lexeme;

                if (CurrentEnvironment.TryGetValue(variableName, out var setVal) && setVal is RestrictedValue)
                {
                    throw new LangException($"Variable '{variableName}' is restricted and cannot be modified", s.Name.Line, _filePath);
                }

                CurrentEnvironment[s.Name.Lexeme] = Evaluate(s.Value);
                break;

            case Statement.SetGlobal sg:
                var key = sg.Name.Lexeme;

                if (_globalEnvironment.TryGetValue(key, out var setGlobalVal) == false)
                {
                    throw new LangException($"Global variable '{key}' is not defined", sg.Name.Line, _filePath);
                }

                if (setGlobalVal is RestrictedValue)
                {
                    throw new LangException($"Global variable '{key}' is restricted and cannot be modified", sg.Name.Line, _filePath);
                }

                _globalEnvironment[key] = Evaluate(sg.Value);
                break;

            case Statement.SetField sf:
                Dictionary<string, object?>? targetScope = null;
                object? existingVal = null;

                if (sf.IsGlobal)
                {
                    if (!_globalEnvironment.TryGetValue(sf.Variable.Lexeme, out existingVal))
                    {
                        throw new LangException($"Global variable '{sf.Variable.Lexeme}' is not defined", sf.Variable.Line, _filePath);
                    }
                    targetScope = _globalEnvironment;
                }
                else
                {
                    for (int i = _scopes.Count - 1; i >= 0; i--)
                    {
                        if (_scopes[i].TryGetValue(sf.Variable.Lexeme, out existingVal))
                        {
                            targetScope = _scopes[i];
                            break;
                        }
                    }

                    if (targetScope == null)
                    {
                        throw new LangException($"Unknown variable '{sf.Variable.Lexeme}'.", sf.Variable.Line, _filePath);
                    }
                }

                if (existingVal is RestrictedValue)
                {
                    string prefix = sf.IsGlobal ? "Global variable" : "Variable";
                    throw new LangException($"{prefix} '{sf.Variable.Lexeme}' is restricted and cannot be modified", sf.Variable.Line, _filePath);
                }

                if (existingVal is not StructInstance)
                {
                    throw new LangException($"Cannot set field '{sf.Fields[0].Lexeme}' on a non-struct value (got '{GetValueType(existingVal)}')", sf.Fields[0].Line, _filePath);
                }

                object? newFieldValue = Evaluate(sf.Value);

                existingVal = targetScope[sf.Variable.Lexeme];
                if (existingVal is RestrictedValue)
                {
                    string prefix = sf.IsGlobal ? "Global variable" : "Variable";
                    throw new LangException($"{prefix} '{sf.Variable.Lexeme}' is restricted and cannot be modified", sf.Variable.Line, _filePath);
                }

                if (existingVal is not StructInstance currentStruct)
                {
                    throw new LangException($"Cannot set field '{sf.Fields[0].Lexeme}' on a non-struct value (got '{GetValueType(existingVal)}')", sf.Fields[0].Line, _filePath);
                }

                StructInstance updatedStruct = UpdateStructField(currentStruct, sf.Fields, 0, newFieldValue);
                targetScope[sf.Variable.Lexeme] = updatedStruct;
                break;
            
            case Statement.Restrict r:
                var restrictedVariableName = r.Name.Lexeme;

                if (CurrentEnvironment.TryGetValue(restrictedVariableName, out var restrictVal) == false)
                {
                    throw new LangException($"Variable '{restrictedVariableName}' is not defined", r.Name.Line, _filePath);
                }

                if (restrictVal is RestrictedValue)
                {
                    throw new LangException($"Variable '{restrictedVariableName}' is already restricted", r.Name.Line, _filePath);
                }

                if (restrictVal != null)
                {
                    CurrentEnvironment[restrictedVariableName] = new RestrictedValue(restrictVal);
                }
                break;

            case Statement.If i:
                if (IsTruthy(Evaluate(i.Condition)))
                {
                    foreach (var s in i.ThenBranch)
                    {
                        Execute(s);
                        if (_returning) break;
                    }
                }
                else
                {
                    bool matched = false;
                    foreach (var e in i.ElsifBranches)
                    {
                        if (IsTruthy(Evaluate(e.Condition)))
                        {
                            foreach (var s in e.Body)
                            {
                                Execute(s);
                                if (_returning) break;
                            }
                            matched = true;
                            break;
                        }
                    }
                    if (!matched && i.ElseBranch != null)
                    {
                        foreach (var s in i.ElseBranch)
                        {
                            Execute(s);
                            if (_returning) break;
                        }
                    }
                }
                break;

            case Statement.Break:
                throw new BreakException();

            case Statement.Continue:
                throw new ContinueException();

            case Statement.While w:
                try
                {
                    while (IsTruthy(Evaluate(w.Condition)) && !_returning)
                    {
                        try
                        {
                            foreach (var s in w.Body)
                            {
                                Execute(s);
                                if (_returning) break;
                            }
                        }
                        catch (ContinueException) { }
                    }
                }
                catch (BreakException) { }
                break;

            case Statement.ForIn fi:
                object? collectionObj = Evaluate(fi.Collection);
                IEnumerable<object?> iterable;

                string strItem = fi.Variable.Lexeme;
                bool isItemValidOutOfScope = CurrentEnvironment.ContainsKey(strItem);

                if (collectionObj is List<object?> list)
                {
                    iterable = list;
                }
                else if (collectionObj is Dictionary<object, object> map) 
                {
                    var mapList = new List<object?>(map.Count);
                    foreach (var ele in map)
                    {
                        mapList.Add(new MapEntry(ele.Key, ele.Value));
                    }

                    iterable = mapList;
                }
                else if (collectionObj is string str)
                {
                    iterable = str.EnumerateRunes().Select(r => (object?)r.ToString());
                }
                else
                {
                    throw new LangException($"'for...in' loop expects a list or a map, but got '{GetValueType(collectionObj)}'", fi.Variable.Line, _filePath);
                }

                try
                {
                    foreach (var item in iterable)
                    {
                        CurrentEnvironment[strItem] = item;
                        if (_returning) break;
                        try
                        {
                            foreach (var s in fi.Body)
                            {
                                Execute(s);
                                if (_returning) break;
                            }
                        }
                        catch (ContinueException) { }
                    }
                }
                catch (BreakException) { }

                if (isItemValidOutOfScope == false)
                    {
                        CurrentEnvironment.Remove(strItem);
                    }

                break;

            case Statement.Function f:
                var functionKey = FunctionKey(f.Name.Lexeme, f.Params);

                if (_functions.ContainsKey(functionKey))
                {
                    throw new LangException($"Function '{f.Name.Lexeme}' with the same parameter types is already defined", f.Name.Line, _filePath);
                }

                _functions[functionKey] = (f.Params, f.Body);

                // precompute the overload metadata once at definition time
                var functionInfo = new UserFunctionInfo(f.Params, f.Body);
                if (_functionOverloads.TryGetValue(f.Name.Lexeme, out var overloads))
                {
                    overloads.Add(functionInfo);
                }
                else
                {
                    _functionOverloads[f.Name.Lexeme] = [functionInfo];
                }

                if (f.IsExported)
                {
                    _exportedFunctionNames.Add(f.Name.Lexeme);
                }

                _resolutionGeneration++;
                break;

            case Statement.Struct s:
                var structKey = s.Name.Lexeme;

                // look into existing struct definitions and check for reserved interpreter structs
                if (_structs.ContainsKey(structKey) || structKey == "Result" || structKey == "MapEntry" || structKey == "HttpResponse")
                {
                    throw new LangException($"Struct '{s.Name.Lexeme}' is already defined", s.Name.Line, _filePath);
                }

                // evaluate the body once at definition time to cache default field values
                var defaults = new Dictionary<string, object?>(StringComparer.Ordinal);
                _scopes.Add(new Dictionary<string, object?>(StringComparer.Ordinal));
                try
                {
                    foreach (var stmt in s.Body)
                    {
                        Execute(stmt);
                    }

                    foreach (var kv in _scopes[_scopes.Count - 1])
                    {
                        defaults[kv.Key] = kv.Value;
                    }
                }
                finally
                {
                    _scopes.RemoveAt(_scopes.Count - 1);
                }

                _structs[s.Name.Lexeme] = defaults;

                if (s.IsExported)
                {
                    _exportedStructNames.Add(s.Name.Lexeme);
                }

                _resolutionGeneration++;
                break;

            case Statement.Expression e:
                object? result = Evaluate(e.Value);
                if (_isInteractiveMode && result != null)
                {
                    Console.WriteLine(Stringify(result));
                }
                break;

            case Statement.Use u:
                object? value = Evaluate(u.Value);

                if (value is not string)
                {
                    throw new LangException($"'use' statement expects a string as module path, but got '{GetValueType(value)}'");
                }

                string modulePath = (string)value;

                bool isInternalModule = modulePath.Equals("jcore/http", StringComparison.OrdinalIgnoreCase);
                if (isInternalModule == false && modulePath.EndsWith(".jask") == false)
                {
                    modulePath += ".jask";
                }

                if (_modules.ContainsKey(u.Alias.Lexeme))
                {
                    throw new LangException($"Module alias '{u.Alias.Lexeme}' is already in use", u.Alias.Line, _filePath);
                }

                if (isInternalModule)
                {
                    EnsureInternalFunctionGroupLoaded(modulePath);

                    var moduleInterpreter = new Interpreter(_modulesLoading, _baseDirectory, _processDirectory, modulePath, _permissionManager);
                    moduleInterpreter.EnsureInternalFunctionGroupLoaded(modulePath);
                    _modules[u.Alias.Lexeme] = moduleInterpreter;
                    break;
                }

                // try embedded jcore modules first
                if (modulePath.StartsWith("jcore/"))
                {
                    string? embeddedSource = TryLoadEmbeddedModule(modulePath);
                    if (embeddedSource != null)
                    {
                        if (_modulesLoading.Contains(modulePath))
                        {
                            throw new LangException($"Circular 'use' detected: module '{modulePath}' is already being loaded", u.Alias.Line, _filePath);
                        }

                        string virtualPath = $"[jcore]/{modulePath}";
                        _modulesLoading.Add(virtualPath);
                        try
                        {
                            var moduleInterpreter = new Interpreter(_modulesLoading, _baseDirectory, _processDirectory, $"jcore/{modulePath}", _permissionManager);
                            var lexer = new Lexer(embeddedSource, false, $"jcore/{modulePath}");
                            var tokens = lexer.ScanTokens();
                            var parser = new Parser(tokens, $"jcore/{modulePath}");
                            var moduleStatements = parser.Parse();
                            moduleInterpreter.Interpret(moduleStatements);

                            _modules[u.Alias.Lexeme] = moduleInterpreter;
                        }
                        finally
                        {
                            _modulesLoading.Remove(virtualPath);
                        }

                        break;
                    }
                }

                // fall back to file system
                if (_permissionManager.IsPermitted(Permission.FileRead) == false)
                {
                    throw new LangException("Missing permission 'read' for loading modules", u.Alias.Line, _filePath);
                }

                string fullPath = ResolveModulePath(modulePath);

                if (_permissionManager.IsPathPermitted(Permission.FileRead, fullPath) == false)
                {
                    throw new LangException($"Missing permission 'read' on '{fullPath}' for loading module", u.Alias.Line, _filePath);
                }

                if (File.Exists(fullPath) == false)
                {
                    throw new LangException($"Module at '{modulePath}' could not be found", u.Alias.Line, _filePath);
                }

                if (_modulesLoading.Contains(fullPath))
                {
                    throw new LangException($"Circular 'use' detected: module '{modulePath}' is already being loaded", u.Alias.Line, _filePath);
                }

                _modulesLoading.Add(fullPath);
                try
                {
                    var moduleInterpreter = new Interpreter(_modulesLoading, Path.GetDirectoryName(fullPath) ?? _baseDirectory, _processDirectory, fullPath, _permissionManager);
                    var lexer = new Lexer(File.ReadAllText(fullPath), false, fullPath);
                    var tokens = lexer.ScanTokens();
                    var parser = new Parser(tokens, fullPath);
                    var moduleStatements = parser.Parse();
                    moduleInterpreter.Interpret(moduleStatements);

                    _modules[u.Alias.Lexeme] = moduleInterpreter;
                }
                finally
                {
                    _modulesLoading.Remove(fullPath);
                }
                break;

            case Statement.Return r:
                _returnValue = r.Value != null ? Evaluate(r.Value) : null;
                _returning = true;
                break;

            case Statement.TryCatch tc:
                try
                {
                    foreach (var s in tc.Body)
                    {
                        Execute(s);
                        if (_returning) break;
                    }
                }
                catch (LangException le)
                {
                    var errorFields = new Dictionary<string, object?>
                    {
                        { "message", le.Message },
                        { "line", (double)le.Line },
                        { "file", (object?)le.FilePath ?? null }
                    };

                    if (tc.ErrorVar != null)
                    {
                        CurrentEnvironment[tc.ErrorVar.Lexeme] = new StructInstance("Error", errorFields);
                    }

                    foreach (var s in tc.CatchBody)
                    {
                        Execute(s);
                        if (_returning) break;
                    }
                }
                break;

            default:
                throw new LangException($"Unknown statement: {statement}", 0, _filePath);
        }
    }

    private StructInstance UpdateStructField(StructInstance current, List<Token> fields, int index, object? newValue)
    {
        Token field = fields[index];
        if (!current.Fields.TryGetValue(field.Lexeme, out var existingFieldValue))
        {
            throw new LangException($"Struct '{current.TypeName}' has no field '{field.Lexeme}'", field.Line, _filePath);
        }

        if (index == fields.Count - 1)
        {
            return current.WithField(field.Lexeme, newValue);
        }

        if (existingFieldValue is not StructInstance childStruct)
        {
            throw new LangException($"Cannot set field '{fields[index + 1].Lexeme}' on a non-struct value (got '{GetValueType(existingFieldValue)}')", fields[index + 1].Line, _filePath);
        }

        StructInstance updatedChild = UpdateStructField(childStruct, fields, index + 1, newValue);
        return current.WithField(field.Lexeme, updatedChild);
    }

    /// <summary>
    /// Resolves a module path in the following order:
    /// 1. if the path is absolute, check if the file exists
    /// 2. if the path is relative, check relative to the current script's directory
    /// 3. if not found, check relative to the process start directory
    /// 4. if the file is not found in any of these locations, returns the first candidate (relative to the current script's directory) for error reporting
    /// </summary>
    private string ResolveModulePath(string modulePath)
    {
        // 1. check if it's an absolute path
        if (Path.IsPathRooted(modulePath))
        {
            string absolutePath = Path.GetFullPath(modulePath);
            if (File.Exists(absolutePath))
            {
                return absolutePath;
            }
        }

        // 2. check relative to the current importing script's directory
        string relativeToScriptPath = Path.GetFullPath(Path.Combine(_baseDirectory, modulePath));
        if (File.Exists(relativeToScriptPath))
        {
            return relativeToScriptPath;
        }

        // 3. check relative to the process start directory
        string relativeToProcessPath = Path.GetFullPath(Path.Combine(_processDirectory, modulePath));
        if (File.Exists(relativeToProcessPath))
        {
            return relativeToProcessPath;
        }

        // if not found anywhere, return the first candidate (relative to current script)
        // this allows the error message to be more informative
        return relativeToScriptPath;
    }

    /// <summary>
    /// Tries to load a .jask module from embedded assembly resources (jcore).
    /// Returns null if not found.
    /// </summary>
    private static string? TryLoadEmbeddedModule(string path)
    {
        string fileName = Path.GetFileName(path);
        string resourceName = $"JaskInterpreter.{fileName}";

        using Stream? stream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
        if (stream == null) return null;

        using StreamReader reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
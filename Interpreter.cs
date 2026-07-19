namespace JaskLang;

public class ReturnException : Exception
{
    public object? Value { get; }
    public ReturnException(object? value) : base() { Value = value; }
}

public class BreakException : Exception
{
    public BreakException() : base() { }
}

public class ContinueException : Exception
{
    public ContinueException() : base() { }
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
    private readonly Dictionary<string, (List<(Token Name, Token Type)> Params, List<Statement> Body)> _functions = [];

    // dictionary for struct definitions: name -> body statements
    private readonly Dictionary<string, List<Statement>> _structs = [];

    // dictionary for imported modules: alias -> isolated interpreter instance running that module
    private readonly Dictionary<string, Interpreter> _modules = [];

    // tracks module file paths currently being loaded (by full path), to detect circular 'use' chains
    private readonly HashSet<string> _modulesLoading;

    // base directory used to resolve relative module paths (directory of the current script)
    private readonly string _baseDirectory;

    // initial process directory used as fallback for module resolution
    private readonly string _processDirectory;

    // current file path for error reporting
    private readonly string? _filePath;

    // stack for environments to manage scopes
    private readonly Stack<Dictionary<string, object?>> _scopes = new();
    
    private Dictionary<string, object?> _globalEnvironment = [];

    private Dictionary<string, object?> CurrentEnvironment => _scopes.Peek();

    public Interpreter() : this(new HashSet<string>(), Directory.GetCurrentDirectory(), Directory.GetCurrentDirectory(), null) { }

    public Interpreter(string baseDirectory, string? filePath) : this(new HashSet<string>(), baseDirectory, Directory.GetCurrentDirectory(), filePath) { }

    // internal constructor used when loading a module, so the circular-import guard is shared across the whole chain
    private Interpreter(HashSet<string> modulesLoading, string baseDirectory, string processDirectory, string? filePath)
    {
        _modulesLoading = modulesLoading;
        _baseDirectory = baseDirectory;
        _processDirectory = processDirectory;
        _filePath = filePath;
        _scopes.Push(_globalEnvironment);
        initInternalFunctions();
    }

    public void Interpret(List<Statement> statements)
    {
        foreach (var statement in statements)
        {
            Execute(statement);
        }
    }

    private void Execute(Statement statement)
    {
        switch (statement)
        {
            case Statement.Set s:
                var variableName = s.Name.Lexeme;

                if (char.IsUpper(variableName[0]))
                {
                    throw new LangException($"Variable '{variableName}' must start with a lowercase letter", s.Name.Line, _filePath);
                }

                if (CurrentEnvironment.TryGetValue(variableName, out var setVal) && setVal is RestrictedValue)
                {
                    throw new LangException($"Variable '{variableName}' is restricted and cannot be modified", s.Name.Line, _filePath);
                }

                CurrentEnvironment[s.Name.Lexeme] = Evaluate(s.Value);
                break;

            case Statement.SetGlobal sg:
                var key = sg.Name.Lexeme;

                if (_globalEnvironment.TryGetValue(key, out var setGlobalVal) && setGlobalVal is RestrictedValue)
                {
                    throw new LangException($"Global variable '{key}' is restricted and cannot be modified", sg.Name.Line, _filePath);
                }

                if (_globalEnvironment.ContainsKey(key) == false)
                {
                    throw new LangException($"Global variable '{key}' is not defined", sg.Name.Line, _filePath);
                }

                _globalEnvironment[sg.Name.Lexeme] = Evaluate(sg.Value);
                break;
            
            case Statement.Restrict r:
                var restrictedVariableName = r.Name.Lexeme;

                if (CurrentEnvironment.TryGetValue(restrictedVariableName, out var restrictVal) && restrictVal is RestrictedValue)
                {
                    throw new LangException($"Variable '{restrictedVariableName}' is already restricted", r.Name.Line, _filePath);
                }

                if (CurrentEnvironment.ContainsKey(restrictedVariableName) == false)
                {
                    throw new LangException($"Variable '{restrictedVariableName}' is not defined", r.Name.Line, _filePath);
                }

                object? var = CurrentEnvironment[restrictedVariableName];
                if (var != null)
                {
                    CurrentEnvironment[restrictedVariableName] = new RestrictedValue(var);
                }
                break;

            case Statement.If i:
                if (IsTruthy(Evaluate(i.Condition)))
                {
                    foreach (var s in i.ThenBranch) Execute(s);
                }
                else if (i.ElseBranch != null)
                {
                    foreach (var s in i.ElseBranch) Execute(s);
                }
                break;

            case Statement.Break:
                throw new BreakException();

            case Statement.Continue:
                throw new ContinueException();

            case Statement.While w:
                try
                {
                    while (IsTruthy(Evaluate(w.Condition)))
                    {
                        try
                        {
                            foreach (var s in w.Body) Execute(s);
                        }
                        catch (ContinueException) { }
                    }
                }
                catch (BreakException) { }
                break;

            case Statement.ForIn fi:
                object? collectionObj = Evaluate(fi.Collection);
                if (collectionObj is not List<object?> list)
                {
                    throw new LangException($"'for...in' loop expects a list, but got '{GetValueType(collectionObj)}'", fi.Variable.Line, _filePath);
                }

                try
                {
                    foreach (var item in list)
                    {
                        CurrentEnvironment[fi.Variable.Lexeme] = item;
                        try
                        {
                            foreach (var s in fi.Body) Execute(s);
                        }
                        catch (ContinueException) { }
                    }
                }
                catch (BreakException) { }
                break;

            case Statement.RepeatTimes rt:
                double repetitions = CheckNumberStmt(new Token(TokenType.Identifier, "repeat", null, 0), Evaluate(rt.Times), "repeat count");
                try
                {
                    for (int i = 0; i < repetitions; i++)
                    {
                        try
                        {
                            Evaluate(rt.Body);
                        }
                        catch (ContinueException) { }
                    }
                }
                catch (BreakException) { }
                break;

            case Statement.Function f:
                var functionKey = FunctionKey(f.Name.Lexeme, f.Params);

                if (_functions.ContainsKey(functionKey))
                {
                    throw new LangException($"Function '{f.Name.Lexeme}' with the same parameter types is already defined", f.Name.Line, _filePath);
                }

                if (char.IsUpper(f.Name.Lexeme[0]))
                {
                    throw new LangException($"Function '{f.Name.Lexeme}' must start with a lowercase letter", f.Name.Line, _filePath);
                }

                _functions[FunctionKey(f.Name.Lexeme, f.Params)] = (f.Params, f.Body);
                break;

            case Statement.Struct s:
                var structKey = s.Name.Lexeme;

                if (_structs.ContainsKey(structKey))
                {
                    throw new LangException($"Struct '{s.Name.Lexeme}' is already defined", s.Name.Line, _filePath);
                }

                if (char.IsUpper(structKey[0]) == false)
                {
                    throw new LangException($"Struct definition for '{s.Name.Lexeme}' must start with an uppercase letter", s.Name.Line, _filePath);
                }

                _structs[s.Name.Lexeme] = s.Body;
                break;

            case Statement.StructUpdate su:
                object? sourceObj = Evaluate(su.Source);
                if (sourceObj is not StructInstance sourceInstance)
                {
                    throw new LangException($"'update' expects a struct instance, but got '{GetValueType(sourceObj)}'", su.Target.Line, _filePath);
                }

                if (CurrentEnvironment.TryGetValue(su.Target.Lexeme, out var structObj) && structObj is RestrictedValue)
                {
                    throw new LangException($"Variable '{su.Target.Lexeme}' is restricted and cannot be modified", su.Target.Line, _filePath);
                }

                // fold each update over the instance, producing a new copy each time
                StructInstance updated = sourceInstance;
                foreach (var (field, valueExpr) in su.Updates)
                {
                    if (!updated.Fields.ContainsKey(field.Lexeme))
                    {
                        throw new LangException($"Struct '{updated.TypeName}' has no field '{field.Lexeme}'", field.Line, _filePath);
                    }
                    updated = updated.WithField(field.Lexeme, Evaluate(valueExpr));
                }

                CurrentEnvironment[su.Target.Lexeme] = updated;
                break;

            case Statement.Expression e:
                Evaluate(e.Value);
                break;

            case Statement.Use u:
                object? value = Evaluate(u.Value);

                if (value is not string)
                {
                    throw new LangException($"'use' statement expects a string as module path, but got '{GetValueType(value)}'");
                }

                string modulePath = (string)value;

                if (modulePath.EndsWith(".jask") == false)
                {
                    modulePath += ".jask";
                }

                string fullPath = ResolveModulePath(modulePath);

                if (File.Exists(fullPath) == false)
                {
                    throw new LangException($"Module at '{modulePath}' could not be found", u.Alias.Line, _filePath);
                }

                if (_modulesLoading.Contains(fullPath))
                {
                    throw new LangException($"Circular 'use' detected: module '{modulePath}' is already being loaded", u.Alias.Line, _filePath);
                }

                if (char.IsUpper(u.Alias.Lexeme[0]) == true)
                {
                    throw new LangException($"Module alias '{u.Alias.Lexeme}' must start with a lowercase letter", u.Alias.Line, _filePath);
                }

                if (_modules.ContainsKey(u.Alias.Lexeme))
                {
                    throw new LangException($"Module alias '{u.Alias.Lexeme}' is already in use", u.Alias.Line, _filePath);
                }

                _modulesLoading.Add(fullPath);
                try
                {
                    var moduleInterpreter = new Interpreter(_modulesLoading, Path.GetDirectoryName(fullPath) ?? _baseDirectory, _processDirectory, fullPath);
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
                object? returnValue = r.Value != null ? Evaluate(r.Value) : null;
                throw new ReturnException(returnValue);

            default:
                throw new LangException($"Unknown statement: {statement}", 0, _filePath);
        }
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
}
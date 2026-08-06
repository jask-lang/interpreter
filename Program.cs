using JaskLang;

const string JASK_VERSION = "0.0.1";

static void printHelpMessage()
{
    Repl.PrintVersionMessage(JASK_VERSION);

    Console.WriteLine("jask [arguments] --input \"file.jask\"" + Environment.NewLine);
    Console.WriteLine("Possible arguments:");
    Console.WriteLine("--help           -h  : Prints this message and exits");
    Console.WriteLine("--version        -v  : Prints the interpreters version and exits");
    Console.WriteLine("--input          -i  : Specifies the path to a .jask file to interpret");
    Console.WriteLine("--allow-stdout   -ao : Allows printing to stdout");
    Console.WriteLine("--allow-stdin    -ai : Allows reading from stdin");
    Console.WriteLine("--allow-trust    -at : Allows using trust()");
    Console.WriteLine("--allow-read     -ar : Allows reading files. Can be specified multiple times for different paths or files");
    Console.WriteLine("--allow-write    -aw : Allows writing files. Can be specified multiple times for different paths or files");
}

ArgumentsParser argumentParser = new ArgumentsParser(args);
PermissionManager permissionManager = new PermissionManager(argumentParser);

// we are only printing the version and then exit the interpreter
if (argumentParser.Has("--version") ||
    argumentParser.Has("-v"))
{
    Repl.PrintVersionMessage(JASK_VERSION);
    return;
}

// we are only printing the help output and then exit the interpreter
if (argumentParser.Has("--help") ||
    argumentParser.Has("-h"))
{
    printHelpMessage();
    return;
}

// we are interpreting a file
if (argumentParser.Has("--input") ||
    argumentParser.Has("-i"))
{
    string file = argumentParser.GetValues(argumentParser.Has("--input") ? "--input" : "-i").ElementAt(0);

    if (File.Exists(file) == false)
    {
        Console.Error.WriteLine($"Input '{file}' cannot be found.");
        return;
    }

    if (Path.GetExtension(file) != ".jask")
    {
        Console.Error.WriteLine($"Input '{file}' is not a jask file.");
        return;
    }

    string fullPath = Path.GetFullPath(file);
    string baseDirectory = Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory();

    Run(new Interpreter(baseDirectory, fullPath, permissionManager), false, File.ReadAllText(fullPath), fullPath);
}
// we are using the interactive mode
else
{
    Repl.Run(permissionManager, JASK_VERSION);
}

static void Run(Interpreter interpreter, bool isInteractiveMode, string source, string? filePath = null)
{
    try
    {
        var lexer = new Lexer(source, isInteractiveMode, filePath);
        var tokens = lexer.ScanTokens();

        var parser = new Parser(tokens, filePath);
        var statements = parser.Parse();

        interpreter.Interpret(statements);
    }
    catch (LangException ex)
    {
        EnsureNewLineBeforeError();
        Console.Error.WriteLine($"\x1b[31mError: \x1b[0m{ex.Message}");
    }
    catch (Exception ex)
    {
        EnsureNewLineBeforeError();
        Console.Error.WriteLine($"\x1b[31mUnexpected error: \x1b[0m{ex.Message}");
    }
}

// helper to ensure, that errors are always printed on a newline
static void EnsureNewLineBeforeError()
{
    try
    {
        if (Console.CursorLeft > 0)
        {
            Console.Error.WriteLine();
        }
    }
    catch (IOException)
    {
        // if stderr has been redirected to a file, CursorLeft will fail
        // in this case, a newline is always added
        Console.Error.WriteLine();
    }
}
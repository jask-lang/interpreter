using System.Text;
using JaskLang;

const String JASK_VERSION = "0.0.1";

static void printVersionMessage()
{
    Console.WriteLine($"jask lang interpreter {JASK_VERSION}");
}

ArgumentsParser argumentParser = new ArgumentsParser(args);
PermissionManager permissionManager = new PermissionManager(argumentParser);

// when are only printing the version and then exit the interpreter
if (argumentParser.Has("--version"))
{
    printVersionMessage();
    return;
}


// we are interpreting a file
if (argumentParser.Has("--input"))
{
    string file = argumentParser.GetValues("--input").ElementAt(0);

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
    RunInteractiveMode(permissionManager);
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
        Console.Error.WriteLine($"Unexpected error: {ex.Message}");
    }
}

static void RunInteractiveMode(PermissionManager permissionManager)
{
    printVersionMessage();
    Console.WriteLine("Use arrow keys for history, type 'exit' when you are done.");

    var interpreter = new Interpreter(permissionManager);
    List<string> history = new List<string>();
    
    StringBuilder multiLineBuffer = new StringBuilder();

    var blockPairs = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        { "endif", "if" },
        { "endwhile", "while" },
        { "endstruct", "struct"},
        { "endfor", "for"},
        { "end", "function" }
    };

    string[] allKeywords = blockPairs.Values.Concat(blockPairs.Keys).ToArray();
    Stack<string> openBlocks = new Stack<string>();

    while (true)
    {
        int indentationLevel = openBlocks.Count;

        if (indentationLevel > 0)
        {
            Console.Write("... " + new string(' ', indentationLevel * 4));
        }
        else
        {
            Console.Write(">>> ");
        }

        string line = ReadLine(history);

        if (line.Trim() == "exit")
        {
            break;
        }

        // add line to history only if it's not the same as the last command
        if (string.IsNullOrWhiteSpace(line))
        {
            continue;
        }

        if (history.Count == 0 || history[history.Count - 1] != line)
        {
            history.Add(line);
        }

        var keywordsInLine = FindKeywordsInOrderOutsideQuotes(line, allKeywords);

        foreach (var token in keywordsInLine)
        {
            if (blockPairs.TryGetValue(token, out string? expectedOpener))
            {
                if (openBlocks.Count > 0 && openBlocks.Peek() == expectedOpener)
                {
                    openBlocks.Pop();
                }
            }
            else
            {
                openBlocks.Push(token);
            }
        }

        if (multiLineBuffer.Length > 0)
        {
            multiLineBuffer.AppendLine();
        }

        multiLineBuffer.Append(line);

        // execute all nested blocks after the most outer block has closed
        if (openBlocks.Count == 0)
        {
            Run(interpreter, true, multiLineBuffer.ToString());
            multiLineBuffer.Clear();
        }
    }
}

static List<string> FindKeywordsInOrderOutsideQuotes(string text, string[] keywords)
{
    var foundKeywords = new List<string>();

    if (string.IsNullOrEmpty(text) || keywords.Length == 0)
    {
        return foundKeywords;
    }

    ReadOnlySpan<char> span = text.AsSpan();
    bool inQuotes = false;

    for (int i = 0; i < span.Length; i++)
    {
        char c = span[i];

        if (c == '"')
        {
            if (i > 0 && span[i - 1] == '\\')
            {
                continue; // Maskiertes Anführungszeichen wird übersprungen
            }
            inQuotes = !inQuotes;
            continue;
        }

        if (!inQuotes)
        {
            foreach (var keyword in keywords)
            {
                ReadOnlySpan<char> target = keyword.AsSpan();

                if (i + target.Length <= span.Length)
                {
                    var slice = span.Slice(i, target.Length);
                    if (slice.SequenceEqual(target))
                    {
                        if (IsWholeWord(span, i, target.Length))
                        {
                            foundKeywords.Add(keyword);
                            i += target.Length - 1; // skip keyword in loop
                            break;
                        }
                    }
                }
            }
        }
    }

    return foundKeywords;
}

static bool IsWholeWord(ReadOnlySpan<char> span, int index, int length)
{
    if (index > 0 && char.IsLetterOrDigit(span[index - 1]))
    {
        return false;
    }

    int nextIndex = index + length;
    if (nextIndex < span.Length && char.IsLetterOrDigit(span[nextIndex]))
    {
        return false;
    }

    return true;
}

static string ReadLine(List<string> history)
{
    StringBuilder input = new StringBuilder();
    int historyIndex = history.Count;
    int cursorPosition = 0; // tracks cursor position in line

    // tracks where the line starts in the console, so we can rewrite it correctly
    int startLeft = Console.CursorLeft;
    int startTop = Console.CursorTop;

    while (true)
    {
        ConsoleKeyInfo keyInfo = Console.ReadKey(true);

        if (keyInfo.Key == ConsoleKey.Enter)
        {
            Console.WriteLine();
            return input.ToString();
        }
        else if (keyInfo.Key == ConsoleKey.LeftArrow)
        {
            if (cursorPosition > 0)
            {
                cursorPosition--;
                Console.SetCursorPosition(startLeft + cursorPosition, startTop);
            }
        }
        else if (keyInfo.Key == ConsoleKey.RightArrow)
        {
            if (cursorPosition < input.Length)
            {
                cursorPosition++;
                Console.SetCursorPosition(startLeft + cursorPosition, startTop);
            }
        }
        else if (keyInfo.Key == ConsoleKey.UpArrow)
        {
            if (history.Count > 0 && historyIndex > 0)
            {
                historyIndex--;
                ClearCurrentLine(startLeft, startTop, input.Length);
                input.Clear();
                input.Append(history[historyIndex]);
                Console.Write(input.ToString());
                cursorPosition = input.Length; // set cursor to end of line
            }
        }
        else if (keyInfo.Key == ConsoleKey.DownArrow)
        {
            if (historyIndex < history.Count - 1)
            {
                historyIndex++;
                ClearCurrentLine(startLeft, startTop, input.Length);
                input.Clear();
                input.Append(history[historyIndex]);
                Console.Write(input.ToString());
                cursorPosition = input.Length;
            }
            else if (historyIndex == history.Count - 1)
            {
                historyIndex++;
                ClearCurrentLine(startLeft, startTop, input.Length);
                input.Clear();
                cursorPosition = 0;
            }
        }
        else if (keyInfo.Key == ConsoleKey.Backspace)
        {
            if (cursorPosition > 0)
            {
                // remove character before the cursor)
                input.Remove(cursorPosition - 1, 1);
                cursorPosition--;

                // rewrite the line and update the cursor position
                RewriteLine(startLeft, startTop, input.ToString(), cursorPosition);
            }
        }
        else if (keyInfo.KeyChar != '\u0000')
        {
            // add char at current cursor position (not necessarily at the end)
            input.Insert(cursorPosition, keyInfo.KeyChar);
            cursorPosition++;

            // rewrite the line and update the cursor position
            RewriteLine(startLeft, startTop, input.ToString(), cursorPosition);
        }
    }
}

// rewrites a line in the console, ensuring the cursor is placed correctly after the rewrite
static void RewriteLine(int startLeft, int startTop, string text, int cursorPosition)
{
    Console.SetCursorPosition(startLeft, startTop);
    Console.Write(text + " ");
    Console.SetCursorPosition(startLeft + cursorPosition, startTop);
}

// deletes the current line in the console, ensuring the cursor is placed at the start of the line after clearing
static void ClearCurrentLine(int startLeft, int startTop, int length)
{
    Console.SetCursorPosition(startLeft, startTop);
    Console.Write(new string(' ', length + 1));
    Console.SetCursorPosition(startLeft, startTop);
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
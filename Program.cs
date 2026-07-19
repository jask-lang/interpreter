using System.Text;
using JaskLang;

// first arg should be a .jask file, otherwise run in interactive mode
if (args.Length == 1)
{
    string file = args[0];

    if (File.Exists(file) == false)
    {
        Console.Error.WriteLine($"File '{file}' not found.");
        return;
    }

    if (Path.GetExtension(file) != ".jask")
    {
        Console.Error.WriteLine($"File '{file}' is not a jask file.");
        return;
    }

    string fullPath = Path.GetFullPath(file);
    string baseDirectory = Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory();
    Run(new Interpreter(baseDirectory, fullPath), false, File.ReadAllText(fullPath), fullPath);
}
else
{
    RunInteractiveMode();
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
        Console.Error.WriteLine($"\x1b[31mError: \x1b[0m{ex.Message}");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Unexpected error: {ex.Message}");
    }
}

static void RunInteractiveMode()
{
    Console.WriteLine("jask lang interpreter 0.0.1");
    Console.WriteLine("Use arrow keys for history, type 'exit' when you are done.");

    var interpreter = new Interpreter();
    List<string> history = new List<string>();

    while (true)
    {
        Console.Write(">>> ");

        string line = ReadLine(history);

        if (string.IsNullOrWhiteSpace(line))
        {
            continue;
        }

        if (line.Trim() == "exit")
        {
            break;
        }

        // add line to history only if it's not the same as the last command
        if (history.Count == 0 || history[history.Count - 1] != line)
        {
            history.Add(line);
        }

        Run(interpreter, true, line);
    }
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
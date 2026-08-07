using System.Reflection;
using System.Text;
using JaskLang;

static class Repl
{
    static readonly Dictionary<TokenType, string> TokenColors = new()
    {
        [TokenType.String]  = "\x1b[31m",
        [TokenType.Number]  = "\x1b[32m",
        [TokenType.True]    = "\x1b[36m",
        [TokenType.False]   = "\x1b[36m",
        [TokenType.Nil]     = "\x1b[36m",
    };

    static readonly HashSet<TokenType> KeywordTypes = new()
    {
        TokenType.Set, TokenType.Restrict, TokenType.Global,
        TokenType.In, TokenType.If, TokenType.Else, TokenType.EndIf,
        TokenType.While, TokenType.EndWhile,
        TokenType.For, TokenType.EndFor,
        TokenType.Function, TokenType.EndFunction,
        TokenType.Use, TokenType.As,
        TokenType.Struct, TokenType.EndStruct,
        TokenType.Update, TokenType.Return,
        TokenType.Break, TokenType.Continue,
        TokenType.And, TokenType.Or, TokenType.Not, TokenType.Is,
    };

    static string GetColorForType(TokenType type)
    {
        if (TokenColors.TryGetValue(type, out var color))
            return color;
        if (KeywordTypes.Contains(type))
            return "\x1b[36m";
        return "\x1b[0m";
    }

    static string Highlight(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        try
        {
            var lexer = new Lexer(text, true, null);
            var tokens = lexer.ScanTokens();
            var sb = new StringBuilder();
            int pos = 0;

            foreach (var token in tokens)
            {
                if (token.Type == TokenType.Eof)
                    break;

                int tokenStart = token.Start;
                while (pos < text.Length && pos < tokenStart)
                {
                    sb.Append(text[pos]);
                    pos++;
                }

                string color = GetColorForType(token.Type);
                sb.Append(color);
                sb.Append(token.Lexeme);
                sb.Append("\x1b[0m");
                pos += token.Lexeme.Length;
            }

            while (pos < text.Length)
            {
                sb.Append(text[pos]);
                pos++;
            }

            return sb.ToString();
        }
        catch
        {
            return text;
        }
    }

    static int VisibleLength(string ansiText)
    {
        int len = 0;
        for (int i = 0; i < ansiText.Length; i++)
        {
            if (ansiText[i] == '\x1b')
            {
                while (i < ansiText.Length && ansiText[i] != 'm')
                    i++;
            }
            else
            {
                len++;
            }
        }
        return len;
    }

    static int VisibleColumnAt(string ansiText, int strIndex)
    {
        int visible = 0;
        int strPos = 0;
        for (int i = 0; i < ansiText.Length; i++)
        {
            if (strPos >= strIndex)
                break;

            if (ansiText[i] == '\x1b')
            {
                while (i < ansiText.Length && ansiText[i] != 'm')
                    i++;
            }
            else
            {
                visible++;
                strPos++;
            }
        }
        return visible;
    }

    static void WriteHighlightedLine(int startLeft, int startTop, string text, int cursorPosition)
    {
        string highlighted = Highlight(text);
        Console.SetCursorPosition(startLeft, startTop);
        Console.Write(highlighted + " ");

        int visibleCol = VisibleColumnAt(highlighted, cursorPosition);
        Console.SetCursorPosition(startLeft + visibleCol, startTop);
    }

    public static void Run(PermissionManager permissionManager, string version)
    {
        PrintVersionMessage(version);
        Console.WriteLine("Use arrow keys for history and Ctrl+l to clear, type 'exit' when you are done.");

        if (permissionManager.IsPermitted(Permission.Stdout) == false)
        {
            Console.WriteLine($"\x1b[33mWarning: \x1b[0mMissing permission for 'stdout'!");
        }

        // .jask_history will be stored in the users home dir
        string homePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string historyFilePath = Path.Combine(homePath, ".jask_history");
        bool writeToHistory = false;

        if (File.Exists(historyFilePath) == false)
        {
            File.Create(historyFilePath).Close();
        }

        string[] historyContent = [];

        try
        {
            historyContent = File.ReadAllLines(historyFilePath);
            writeToHistory = true;
        }
        catch
        {
            // something went wrong so we skip trying to write to the history file further in the code
            writeToHistory = false;
        }

        var interpreter = new Interpreter(permissionManager, isInteractiveMode: true);
        List<string> history = historyContent.ToList();
        StringBuilder multiLineBuffer = new StringBuilder();

        var blockPairs = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "endif", "if" },
            { "endwhile", "while" },
            { "endstruct", "struct" },
            { "endfor", "for" },
            { "endfunction", "function" }
        };

        string[] allKeywords = blockPairs.Values.Concat(blockPairs.Keys).ToArray();
        Stack<string> openBlocks = new Stack<string>();

        while (true)
        {
            int indentationLevel = openBlocks.Count;
            printPrompt(indentationLevel);

            string line = ReadLine(history, indentationLevel);

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

                try
                {
                    if (writeToHistory)
                    {
                        if (history.Count > 100)
                        {
                            history.RemoveAt(0);
                            File.WriteAllLines(historyFilePath, history);
                        }
                        else
                        {
                            File.AppendAllText(historyFilePath, line + Environment.NewLine);
                        }
                    }
                }
                catch { }
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
                RunCommand(interpreter, multiLineBuffer.ToString());
                multiLineBuffer.Clear();
            }
        }
    }

    static void RunCommand(Interpreter interpreter, string source)
    {
        try
        {
            var lexer = new Lexer(source, true, null);
            var tokens = lexer.ScanTokens();
            var parser = new Parser(tokens, null);
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

    public static void PrintVersionMessage(string version)
    {
        Console.WriteLine($"\x1b[38;5;208mjask\x1b[0m lang interpreter {version} (build {GetBuildDate()})");
    }

    static string GetBuildDate()
    {
        var assembly = Assembly.GetExecutingAssembly();
        return assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attr => attr.Key == "BuildDate")
            ?.Value ?? "Unknown";
    }

    static string ReadLine(List<string> history, int indentationLevel)
    {
        StringBuilder input = new StringBuilder();
        int historyIndex = history.Count;

        // tracks cursor position in line
        int cursorPosition = 0;

        int startLeft = Console.CursorLeft;
        int startTop  = Console.CursorTop;

        while (true)
        {
            ConsoleKeyInfo keyInfo = Console.ReadKey(true);

            if ((keyInfo.Modifiers & ConsoleModifiers.Control) == ConsoleModifiers.Control && keyInfo.Key == ConsoleKey.L)
            {
                // clear screen and redraw prompt
                Console.Clear();
                printPrompt(indentationLevel);

                startLeft = Console.CursorLeft;
                startTop  = Console.CursorTop;

                continue;
            }
            else if (keyInfo.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                return input.ToString();
            }
            else if (keyInfo.Key == ConsoleKey.LeftArrow)
            {
                if (cursorPosition > 0)
                {
                    cursorPosition--;
                    int visCol = VisibleColumnAt(Highlight(input.ToString()), cursorPosition);
                    Console.SetCursorPosition(startLeft + visCol, startTop);
                }
            }
            else if (keyInfo.Key == ConsoleKey.RightArrow)
            {
                if (cursorPosition < input.Length)
                {
                    cursorPosition++;
                    int visCol = VisibleColumnAt(Highlight(input.ToString()), cursorPosition);
                    Console.SetCursorPosition(startLeft + visCol, startTop);
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
                    cursorPosition = input.Length;

                    string highlighted = Highlight(input.ToString());
                    Console.Write(highlighted);
                    int visLen = VisibleLength(highlighted);
                    Console.SetCursorPosition(startLeft + visLen, startTop);
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
                    cursorPosition = input.Length;

                    string highlighted = Highlight(input.ToString());
                    Console.Write(highlighted);
                    int visLen = VisibleLength(highlighted);
                    Console.SetCursorPosition(startLeft + visLen, startTop);
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
                    WriteHighlightedLine(startLeft, startTop, input.ToString(), cursorPosition);
                }
            }
            else if (keyInfo.KeyChar != '\u0000')
            {
                // add char at current cursor position (not necessarily at the end)
                input.Insert(cursorPosition, keyInfo.KeyChar);
                cursorPosition++;

                // rewrite the line and update the cursor position
                WriteHighlightedLine(startLeft, startTop, input.ToString(), cursorPosition);
            }
        }
    }

    static void printPrompt(int indentationLevel)
    {
        if (indentationLevel > 0)
        {
            Console.Write("... " + new string(' ', indentationLevel * 4));
        }
        else
        {
            
            Console.Write(">>> ");
        }
    }

    // deletes the current line in the console, ensuring the cursor is placed at the start of the line after clearing
    static void ClearCurrentLine(int startLeft, int startTop, int length)
    {
        Console.SetCursorPosition(startLeft, startTop);
        Console.Write(new string(' ', length + 1));
        Console.SetCursorPosition(startLeft, startTop);
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
                    // skip keyword in loop
                    continue;
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
                                i += target.Length - 1;
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
            Console.Error.WriteLine();
        }
    }
}
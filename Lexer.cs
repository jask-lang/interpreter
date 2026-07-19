namespace JaskLang;

public class Lexer
{
    private readonly string _source;
    private readonly string? _filePath;
    private readonly List<Token> _tokens = [];
    private int _start = 0;
    private int _current = 0;
    private int _line = 1;

    private static readonly Dictionary<string, TokenType> Keywords = new()
    {
        ["set"]       = TokenType.Set,
        ["restrict"]  = TokenType.Restrict,
        ["global"]    = TokenType.Global,
        ["in"]        = TokenType.In,
        ["if"]        = TokenType.If,
        ["else"]      = TokenType.Else,
        ["endif"]     = TokenType.EndIf,
        ["while"]     = TokenType.While,
        ["endwhile"]  = TokenType.EndWhile,
        ["for"]       = TokenType.For,
        ["endfor"]    = TokenType.EndFor,
        ["repeat"]    = TokenType.Repeat,
        ["times"]     = TokenType.Times,
        ["true"]      = TokenType.True,
        ["false"]     = TokenType.False,
        ["nil"]       = TokenType.Nil,
        ["function"]  = TokenType.Function,
        ["use"]       = TokenType.Use,
        ["as"]        = TokenType.As,
        ["end"]       = TokenType.End,
        ["struct"]    = TokenType.Struct,
        ["endstruct"] = TokenType.EndStruct,
        ["update"]    = TokenType.Update,
        ["return"]    = TokenType.Return,
        ["break"]     = TokenType.Break,
        ["continue"]  = TokenType.Continue,
        ["and"]       = TokenType.And,
        ["or"]        = TokenType.Or,
        ["not"]       = TokenType.Not,
        ["is"]        = TokenType.Is,
    };

    public Lexer(string source, bool isInteractiveMode, string? filePath = null)
    {
        _source = source;
        _line = isInteractiveMode ? 0 : 1;
        _filePath = filePath;
    }

    public List<Token> ScanTokens()
    {
        while (IsAtEnd() == false)
        {
            _start = _current;
            ScanToken();
        }

        _tokens.Add(new Token(TokenType.Eof, "", null, _line));
        return _tokens;
    }

    private void ScanToken()
    {
        char c = Advance();
        switch (c)
        {
            case '(': AddToken(TokenType.LParen); break;
            case ')': AddToken(TokenType.RParen); break;
            case ':':
                AddToken(Match(':') ? TokenType.ColonColon : TokenType.Colon);
                break;
            case ',': AddToken(TokenType.Comma); break;
            case '+': AddToken(TokenType.Plus); break;
            case '-':
                if (Match('>')) AddToken(TokenType.Arrow);
                else AddToken(TokenType.Minus);
                break;
            case '*': AddToken(TokenType.Star); break;
            case '%': AddToken(TokenType.Modulo); break;
            case '/': AddToken(TokenType.Slash); break;
            case ';':
                if (Match(';'))
                {
                    // Comments multiline - ignore everything in between ;; and ;;
                    while (IsAtEnd() == false)
                    {
                        if (Peek() == '\n')
                        {
                            _line++;
                        }

                        if (Peek() == ';' && PeekNext() == ';')
                        {
                            Advance(); // consume first ';'
                            Advance(); // consume second ';'
                            break;
                        }
                        Advance();
                    }
                }
                else
                {
                    // Comments singleline: ignore everything until the end of the line
                    while (Peek() != '\n' && !IsAtEnd())
                    {
                        Advance();
                    }
                }
                break;
            case '=':
                AddToken(Match('=') ? TokenType.EqualEqual : TokenType.Assign);
                break;
            case '!':
                if (Match('='))
                {
                    AddToken(TokenType.BangEqual);
                }
                else
                {
                    throw new LangException("Unexpected character '!'.", _line, _filePath);
                }
                break;
            case '<':
                AddToken(Match('=') ? TokenType.LessEqual : TokenType.Less);
                break;
            case '>':
                AddToken(Match('=') ? TokenType.GreaterEqual : TokenType.Greater);
                break;
            case ' ':
            case '\r':
            case '\t':
                break;
            case '\n':
                _line++;
                break;
            case '"':
                ScanString();
                break;
            default:
                if (char.IsDigit(c))
                {
                    ScanNumber();
                }
                else if (char.IsLetter(c) || c == '_')
                {
                    ScanIdentifier();
                }
                else
                {
                    throw new LangException($"Unknown character '{c}'.", _line, _filePath);
                }
                break;
        }
    }

    private void ScanString()
    {
        while (Peek() != '"' && IsAtEnd() == false)
        {
            if (Peek() == '\n')
            {
                _line++;
            }
            Advance();
        }

        if (IsAtEnd())
        {
            throw new LangException("Unclosed string.", _line, _filePath);
        }

        // closing "
        Advance();

        string rawValue = _source.Substring(_start + 1, _current - _start - 2);
        string value = ProcessEscapeSequences(rawValue);

        AddToken(TokenType.String, value);
    }

    private string ProcessEscapeSequences(string str)
    {
        var result = new System.Text.StringBuilder();

        for (int i = 0; i < str.Length; i++)
        {
            if (str[i] == '\\' && i + 1 < str.Length)
            {
                switch (str[i + 1])
                {
                    case 'n':
                        result.Append('\n');
                        i++;
                        break;
                    case 't':
                        result.Append('\t');
                        i++;
                        break;
                    case 'r':
                        result.Append('\r');
                        i++;
                        break;
                    case '\\':
                        result.Append('\\');
                        i++;
                        break;
                    case '"':
                        result.Append('"');
                        i++;
                        break;
                    default:
                        result.Append(str[i]);
                        break;
                }
            }
            else
            {
                result.Append(str[i]);
            }
        }

        return result.ToString();
    }

    private void ScanNumber()
    {
        while (char.IsDigit(Peek()))
        {
            Advance();
        }

        if (Peek() == '.' && char.IsDigit(PeekNext()))
        {
            Advance();

            while (char.IsDigit(Peek()))
            {
                Advance();
            }
        }

        string text = _source.Substring(_start, _current - _start);
        AddToken(TokenType.Number, double.Parse(text, System.Globalization.CultureInfo.InvariantCulture));
    }

    private void ScanIdentifier()
    {
        while (char.IsLetterOrDigit(Peek()) || Peek() == '_')
        {
            Advance();
        }

        string text = _source.Substring(_start, _current - _start);
        TokenType type = Keywords.TryGetValue(text, out var kw) ? kw : TokenType.Identifier;
        AddToken(type);
    }

    private bool Match(char expected)
    {
        if (IsAtEnd() || _source[_current] != expected)
        {
            return false;
        }

        _current++;

        return true;
    }

    private char Peek() => IsAtEnd() ? '\0' : _source[_current];
    private char PeekNext() => _current + 1 >= _source.Length ? '\0' : _source[_current + 1];
    private char Advance() => _source[_current++];
    private bool IsAtEnd() => _current >= _source.Length;

    private void AddToken(TokenType type, object? literal = null)
    {
        string text = _source.Substring(_start, _current - _start);
        _tokens.Add(new Token(type, text, literal, _line));
    }
}

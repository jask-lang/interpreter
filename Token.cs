namespace JaskLang;

public enum TokenType
{
    // literals
    Number,
    Identifier,
    String,

    // keywords
    Set,
    Restrict,
    Global,
    In,
    If,
    Else,
    EndIf,
    While,
    EndWhile,
    For,
    EndFor,
    Break,
    Continue,
    True,
    False,
    Nil,
    Function,
    Use,
    As,
    EndFunction,
    Return,
    And,
    Or,
    Not,
    Is,

    // struct keywords
    Struct,
    EndStruct,
    Update,

    // symbols
    Plus,
    Minus,
    Star,
    Slash,
    Modulo,
    LParen,
    RParen,
    LBracket,
    RBracket,
    LSquare,
    RSquare,
    Colon,
    ColonColon,
    Comma,
    Dot,
    Assign,

    // comparison operators
    EqualEqual,
    BangEqual,
    Less,
    Greater,
    LessEqual,
    GreaterEqual,

    Eof
}

public class Token(TokenType type, string lexeme, object? literal, int line)
{
    public TokenType Type { get; } = type;
    public string Lexeme { get; } = lexeme;
    public object? Literal { get; } = literal;
    public int Line { get; } = line;

    public override string ToString() => $"{Type} '{Lexeme}'";
}

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
    Repeat,
    Times,
    Break,
    Continue,
    True,
    False,
    Nil,
    Function,
    Use,
    As,
    End,
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
    Colon,
    ColonColon,
    Comma,
    Arrow,
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

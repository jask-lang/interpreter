namespace JaskLang;

// expressions: everything that evaluates to a value (4, x, x + y, x < y, ...)
public abstract record Expression
{
    public record Literal(object? Value) : Expression;

    public record Variable(Token Name) : Expression;

    public record Unary(Token Op, Expression Right) : Expression;

    public record Binary(Expression Left, Token Op, Expression Right) : Expression;

    public record Grouping(Expression Inner) : Expression;
 
    public record Call(Expression Callee, List<Expression> Arguments) : Expression;
 
    public record NamedCall(Token Name, List<(Token ParamName, JaskLang.Expression Value)> Args) : Expression;
 
    public record ModuleCall(Token ModuleAlias, Token Name, List<Expression> Arguments) : Expression;
 
    public record ModuleNamedCall(Token ModuleAlias, Token Name, List<(Token ParamName, JaskLang.Expression Value)> Args) : Expression;
 
    public record StructCall(Token Name, List<(Token Field, JaskLang.Expression Value)> FieldInits) : Expression;

    public record MemberAccess(JaskLang.Expression Struct, Token Member) : Expression;
}

// everything that is executed but does not produce a value itself
public abstract record Statement
{
    public record Set(Token Name, JaskLang.Expression Value) : Statement;

    public record Restrict(Token Name) : Statement;

    public record SetGlobal(Token Name, JaskLang.Expression Value) : Statement;

    public record StructUpdate(JaskLang.Expression Source, List<(Token Field, JaskLang.Expression Value)> Updates, Token Target) : Statement;

    public record If(JaskLang.Expression Condition, List<Statement> ThenBranch, List<Statement>? ElseBranch) : Statement;

    public record While(JaskLang.Expression Condition, List<Statement> Body) : Statement;

    public record ForIn(Token Variable, JaskLang.Expression Collection, List<Statement> Body) : Statement;

    public record RepeatTimes(JaskLang.Expression Body, JaskLang.Expression Times) : Statement;

    public record Break() : Statement;

    public record Continue() : Statement;

    public record Function(Token Name, List<(Token Name, Token Type, JaskLang.Expression? Default)> Params, List<Statement> Body) : Statement;

    public record Expression(JaskLang.Expression Value) : Statement;

    public record Use(JaskLang.Expression Value, Token Alias) : Statement;

    public record Return(JaskLang.Expression? Value) : Statement;

    public record Struct(Token Name, List<Statement> Body) : Statement;
}

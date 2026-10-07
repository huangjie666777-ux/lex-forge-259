namespace LexForge259;

/// <summary>A grammar symbol: either a terminal (a non-skip lexer rule name) or a non-terminal.</summary>
public abstract record GrammarSymbol
{
    /// <summary>References a non-skip lexer rule by name.</summary>
    public sealed record Terminal(string RuleName) : GrammarSymbol;

    /// <summary>References a non-terminal defined by at least one production.</summary>
    public sealed record NonTerminal(string Name) : GrammarSymbol;
}

/// <summary>
/// A single production: unique <paramref name="Id"/>, left-hand non-terminal
/// <paramref name="Left"/>, and ordered right-hand symbols. An empty
/// <paramref name="Right"/> is an epsilon production.
/// </summary>
public sealed record Production(string Id, string Left, IReadOnlyList<GrammarSymbol> Right);

/// <summary>An ordered parse tree produced by <see cref="Parser.Parse"/>.</summary>
public abstract record ParseNode
{
    /// <summary>
    /// A non-terminal expansion. <paramref name="ProductionId"/> identifies the
    /// applied production; children are in source order. Epsilon productions
    /// yield a node with no children.
    /// </summary>
    public sealed record NonTerminal(string Name, string ProductionId, IReadOnlyList<ParseNode> Children) : ParseNode;

    /// <summary>A terminal leaf preserving the original <see cref="LexToken"/>.</summary>
    public sealed record Terminal(LexToken Token) : ParseNode;
}

/// <summary>Raised when a grammar cannot be compiled into an LL(1) parser.</summary>
public sealed class ParserCompileException : Exception
{
    /// <summary>For prediction-table conflicts: the non-terminal owning the cell.</summary>
    public string? NonTerminal { get; }

    /// <summary>For prediction-table conflicts: the lookahead terminal (<see cref="Parser.EndOfInput"/> for EOF).</summary>
    public string? Lookahead { get; }

    /// <summary>For prediction-table conflicts: the ids of the competing productions.</summary>
    public IReadOnlyList<string>? ConflictingProductions { get; }

    public ParserCompileException(string message) : base(message) { }

    public ParserCompileException(string message, string nonTerminal, string lookahead, IReadOnlyList<string> conflictingProductions)
        : base(message)
    {
        NonTerminal = nonTerminal;
        Lookahead = lookahead;
        ConflictingProductions = conflictingProductions;
    }
}

/// <summary>Raised on the first syntax error; parsing stops immediately.</summary>
public sealed class ParserParseException : Exception
{
    /// <summary>The offending token, or <c>null</c> when input ended early (EOF).</summary>
    public LexToken? Actual { get; }

    /// <summary>Expected terminal names (lexer rule names, or <see cref="Parser.EndOfInput"/>), sorted ordinally.</summary>
    public IReadOnlyList<string> Expected { get; }

    /// <summary>0-based offset of the error; at EOF this is the text length (trailing skipped text included).</summary>
    public int Offset { get; }

    /// <summary>1-based line of the error (only LF advances the line).</summary>
    public int Line { get; }

    /// <summary>1-based column of the error.</summary>
    public int Column { get; }

    public ParserParseException(string message, LexToken? actual, IReadOnlyList<string> expected, int offset, int line, int column)
        : base(message)
    {
        Actual = actual;
        Expected = expected;
        Offset = offset;
        Line = line;
        Column = column;
    }
}

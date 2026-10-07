using LexForge259;
using Xunit;

namespace LexForge259.Tests;

public class ParserTests
{
    private static readonly LexRule[] ScriptRules =
    {
        new("KwIf", "if"),
        new("Ident", "[a-z][a-z0-9]*"),
        new("Number", "[0-9]+"),
        new("Assign", "="),
        new("Semicolon", ";"),
        new("Ws", "[ \\t\\n]+", Skip: true),
    };

    private static readonly Production[] ScriptProductions =
    {
        new("list-cons", "StmtList", new GrammarSymbol[]
        {
            new GrammarSymbol.NonTerminal("Stmt"),
            new GrammarSymbol.NonTerminal("StmtList"),
        }),
        new("list-eps", "StmtList", Array.Empty<GrammarSymbol>()),
        new("stmt-assign", "Stmt", new GrammarSymbol[]
        {
            new GrammarSymbol.Terminal("Ident"),
            new GrammarSymbol.Terminal("Assign"),
            new GrammarSymbol.Terminal("Number"),
            new GrammarSymbol.Terminal("Semicolon"),
        }),
        new("stmt-if", "Stmt", new GrammarSymbol[]
        {
            new GrammarSymbol.Terminal("KwIf"),
            new GrammarSymbol.Terminal("Ident"),
            new GrammarSymbol.Terminal("Semicolon"),
        }),
    };

    private static Parser NewParser() =>
        ParserCompiler.Compile(LexerCompiler.Compile(ScriptRules), "StmtList", ScriptProductions);

    [Fact]
    public void ValidScript_ProducesOrderedTree()
    {
        var parser = NewParser();
        var root = parser.Parse("x = 1; if y;");

        Assert.Equal("StmtList", root.Name);
        Assert.Equal("list-cons", root.ProductionId);
        Assert.Equal(2, root.Children.Count);

        var stmt = Assert.IsType<ParseNode.NonTerminal>(root.Children[0]);
        Assert.Equal("stmt-assign", stmt.ProductionId);
        Assert.Equal(4, stmt.Children.Count);
        var ident = Assert.IsType<ParseNode.Terminal>(stmt.Children[0]);
        Assert.Equal("Ident", ident.Token.Name);
        Assert.Equal("x", ident.Token.Text);
        Assert.Equal(0, ident.Token.Offset);

        var tail = Assert.IsType<ParseNode.NonTerminal>(root.Children[1]);
        var stmt2 = Assert.IsType<ParseNode.NonTerminal>(tail.Children[0]);
        Assert.Equal("stmt-if", stmt2.ProductionId);
        var kw = Assert.IsType<ParseNode.Terminal>(stmt2.Children[0]);
        Assert.Equal("if", kw.Token.Text);
        Assert.Equal(7, kw.Token.Offset);
        Assert.Equal(1, kw.Token.Line);
        Assert.Equal(8, kw.Token.Column);
    }

    [Fact]
    public void EmptyInput_UsesEpsilonProduction_WithNoChildren()
    {
        var parser = NewParser();
        var root = parser.Parse("");
        Assert.Equal("StmtList", root.Name);
        Assert.Equal("list-eps", root.ProductionId);
        Assert.Empty(root.Children);
    }

    [Fact]
    public void SyntaxError_ReportsActualExpectedAndPosition()
    {
        var parser = NewParser();
        var ex = Assert.Throws<ParserParseException>(() => parser.Parse("x = ;"));
        Assert.NotNull(ex.Actual);
        Assert.Equal("Semicolon", ex.Actual!.Name);
        Assert.Equal(new[] { "Number" }, ex.Expected);
        Assert.Equal(4, ex.Offset);
        Assert.Equal(1, ex.Line);
        Assert.Equal(5, ex.Column);
    }

    [Fact]
    public void MissingInput_ReportsEofAtEndIncludingTrailingSkippedText()
    {
        var parser = NewParser();
        var ex = Assert.Throws<ParserParseException>(() => parser.Parse("x = 1"));
        Assert.Null(ex.Actual);
        Assert.Equal(new[] { "Semicolon" }, ex.Expected);
        Assert.Equal(5, ex.Offset);
        Assert.Equal(1, ex.Line);
        Assert.Equal(6, ex.Column);

        var ex2 = Assert.Throws<ParserParseException>(() => parser.Parse("x = 1 \n "));
        Assert.Null(ex2.Actual);
        Assert.Equal(8, ex2.Offset);
        Assert.Equal(2, ex2.Line);
        Assert.Equal(2, ex2.Column);
    }

    [Fact]
    public void TrailingTokens_AreNotAcceptedAsLegalPrefix()
    {
        var parser = NewParser();
        // "if x;" is a complete statement, but the extra ";" cannot start a statement.
        var ex = Assert.Throws<ParserParseException>(() => parser.Parse("if x; ;"));
        Assert.NotNull(ex.Actual);
        Assert.Equal("Semicolon", ex.Actual!.Name);
        Assert.Equal(new[] { Parser.EndOfInput, "Ident", "KwIf" }, ex.Expected);
    }

    [Fact]
    public void LexerError_PropagatesUnchanged()
    {
        var parser = NewParser();
        Assert.Throws<LexerScanException>(() => parser.Parse("x = ?"));
    }

    [Fact]
    public void Conflict_FailsCompilation_WithNonTerminalLookaheadAndProductions()
    {
        var lexer = LexerCompiler.Compile(ScriptRules);
        var productions = new[]
        {
            new Production("a-short", "A", new GrammarSymbol[] { new GrammarSymbol.Terminal("Ident") }),
            new Production("a-long", "A", new GrammarSymbol[] { new GrammarSymbol.Terminal("Ident"), new GrammarSymbol.Terminal("Number") }),
        };
        var ex = Assert.Throws<ParserCompileException>(() => ParserCompiler.Compile(lexer, "A", productions));
        Assert.Equal("A", ex.NonTerminal);
        Assert.Equal("Ident", ex.Lookahead);
        Assert.Equal(new[] { "a-long", "a-short" }, ex.ConflictingProductions);
    }

    [Fact]
    public void DuplicateProductionId_Rejected()
    {
        var lexer = LexerCompiler.Compile(ScriptRules);
        var productions = new[]
        {
            new Production("dup", "A", new GrammarSymbol[] { new GrammarSymbol.Terminal("Ident") }),
            new Production("dup", "B", new GrammarSymbol[] { new GrammarSymbol.Terminal("Number") }),
        };
        Assert.Throws<ParserCompileException>(() => ParserCompiler.Compile(lexer, "A", productions));
    }

    [Fact]
    public void UndefinedReferences_Rejected()
    {
        var lexer = LexerCompiler.Compile(ScriptRules);
        var badTerminal = new[]
        {
            new Production("p1", "A", new GrammarSymbol[] { new GrammarSymbol.Terminal("Nope") }),
        };
        Assert.Throws<ParserCompileException>(() => ParserCompiler.Compile(lexer, "A", badTerminal));

        var badNonTerminal = new[]
        {
            new Production("p1", "A", new GrammarSymbol[] { new GrammarSymbol.NonTerminal("Missing") }),
        };
        Assert.Throws<ParserCompileException>(() => ParserCompiler.Compile(lexer, "A", badNonTerminal));
    }

    [Fact]
    public void InvalidStartSymbol_Rejected()
    {
        var lexer = LexerCompiler.Compile(ScriptRules);
        Assert.Throws<ParserCompileException>(() => ParserCompiler.Compile(lexer, "Nope", ScriptProductions));
    }

    [Fact]
    public void SkipRuleReference_Rejected()
    {
        var lexer = LexerCompiler.Compile(ScriptRules);
        var productions = new[]
        {
            new Production("p1", "A", new GrammarSymbol[] { new GrammarSymbol.Terminal("Ws") }),
        };
        Assert.Throws<ParserCompileException>(() => ParserCompiler.Compile(lexer, "A", productions));
    }

    [Fact]
    public void DirectLeftRecursion_Rejected()
    {
        var lexer = LexerCompiler.Compile(ScriptRules);
        var productions = new[]
        {
            new Production("e-rec", "E", new GrammarSymbol[] { new GrammarSymbol.NonTerminal("E"), new GrammarSymbol.Terminal("Ident") }),
            new Production("e-base", "E", new GrammarSymbol[] { new GrammarSymbol.Terminal("Ident") }),
        };
        var ex = Assert.Throws<ParserCompileException>(() => ParserCompiler.Compile(lexer, "E", productions));
        Assert.Contains("Left recursion", ex.Message);
    }

    [Fact]
    public void IndirectLeftRecursion_Rejected()
    {
        var lexer = LexerCompiler.Compile(ScriptRules);
        var productions = new[]
        {
            new Production("a-b", "A", new GrammarSymbol[] { new GrammarSymbol.NonTerminal("B"), new GrammarSymbol.Terminal("Ident") }),
            new Production("b-a", "B", new GrammarSymbol[] { new GrammarSymbol.NonTerminal("A"), new GrammarSymbol.Terminal("Number") }),
            new Production("b-base", "B", new GrammarSymbol[] { new GrammarSymbol.Terminal("Number") }),
            new Production("a-base", "A", new GrammarSymbol[] { new GrammarSymbol.Terminal("Number") }),
        };
        var ex = Assert.Throws<ParserCompileException>(() => ParserCompiler.Compile(lexer, "A", productions));
        Assert.Contains("Left recursion", ex.Message);
    }

    [Fact]
    public void NullablePrefixLeftRecursion_Rejected()
    {
        // Opt is nullable, so A -> Opt A ... is indirectly left-recursive.
        var lexer = LexerCompiler.Compile(ScriptRules);
        var productions = new[]
        {
            new Production("a-rec", "A", new GrammarSymbol[] { new GrammarSymbol.NonTerminal("Opt"), new GrammarSymbol.NonTerminal("A"), new GrammarSymbol.Terminal("Ident") }),
            new Production("a-base", "A", new GrammarSymbol[] { new GrammarSymbol.Terminal("Number") }),
            new Production("opt-some", "Opt", new GrammarSymbol[] { new GrammarSymbol.Terminal("Semicolon") }),
            new Production("opt-eps", "Opt", Array.Empty<GrammarSymbol>()),
        };
        Assert.Throws<ParserCompileException>(() => ParserCompiler.Compile(lexer, "A", productions));
    }

    [Fact]
    public void RepeatedParses_AreIndependent_AndGrammarIsSnapshotted()
    {
        var lexer = LexerCompiler.Compile(ScriptRules);
        var productions = new List<Production>(ScriptProductions);
        var parser = ParserCompiler.Compile(lexer, "StmtList", productions);
        productions.Clear(); // caller mutation must not affect the compiled parser

        var first = parser.Parse("x = 1;");
        var second = parser.Parse("if y;");
        var third = parser.Parse("x = 1;");
        Assert.Equal("list-cons", first.ProductionId);
        Assert.Equal("stmt-if", Assert.IsType<ParseNode.NonTerminal>(second.Children[0]).ProductionId);
        Assert.Equal(Describe(first), Describe(third)); // identical trees, no cross-parse pollution
    }

    private static string Describe(ParseNode node) => node switch
    {
        ParseNode.Terminal t => t.Token.Name + ":" + t.Token.Text,
        ParseNode.NonTerminal nt => nt.Name + "(" + string.Join(",", nt.Children.Select(Describe)) + ")",
        _ => throw new InvalidOperationException(),
    };
}

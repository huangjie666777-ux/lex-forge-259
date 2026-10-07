namespace LexForge259;

/// <summary>Compiles a structured LL(1) grammar against an existing lexer.</summary>
public static class ParserCompiler
{
    /// <summary>
    /// Validates the grammar, computes nullable/FIRST/FOLLOW and the prediction
    /// table (EOF included), and returns a reusable parser. The production list
    /// is snapshotted; later caller mutations have no effect.
    /// </summary>
    public static Parser Compile(Lexer lexer, string start, IReadOnlyList<Production> productions)
    {
        ArgumentNullException.ThrowIfNull(lexer);
        GrammarModel model = GrammarAnalysis.Analyze(start, productions, lexer);
        PredictTable table = PredictTable.Build(model);
        return new Parser(lexer, model, table);
    }
}

/// <summary>
/// A compiled, immutable LL(1) parser bound to a <see cref="Lexer"/>. The grammar
/// is snapshotted at compile time; repeated <see cref="Parse"/> calls share no
/// mutable state and never pollute each other.
/// </summary>
public sealed class Parser
{
    /// <summary>Name used for the end-of-input marker in expected sets and messages.</summary>
    public const string EndOfInput = "EOF";

    private readonly Lexer _lexer;
    private readonly GrammarModel _model;
    private readonly PredictTable _table;

    internal Parser(Lexer lexer, GrammarModel model, PredictTable table)
    {
        _lexer = lexer;
        _model = model;
        _table = table;
    }

    /// <summary>
    /// Tokenizes <paramref name="text"/> with the bound lexer and parses the full
    /// token stream. Lexer errors surface unchanged as <see cref="LexerScanException"/>.
    /// The whole input must be consumed; a legal prefix is never accepted as success.
    /// </summary>
    public ParseNode.NonTerminal Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        IReadOnlyList<LexToken> tokens = _lexer.Scan(text);

        var root = new NodeBuilder(_model.Start);
        var stack = new Stack<WorkItem>();
        stack.Push(WorkItem.Expand(_model.Start, null));
        int pos = 0;

        while (stack.Count > 0)
        {
            WorkItem item = stack.Pop();
            string? lookahead = pos < tokens.Count ? tokens[pos].Name : null;

            if (item.TerminalRuleName is string expected)
            {
                if (lookahead != expected)
                    throw Error(text, tokens, pos, new[] { expected });
                item.Parent!.Children.Add(new ParseNode.Terminal(tokens[pos]));
                pos++;
                continue;
            }

            string nonTerminal = item.NonTerminal!;
            Production? production = _table.Lookup(nonTerminal, lookahead);
            if (production is null)
                throw Error(text, tokens, pos, _table.ExpectedFor(nonTerminal));

            NodeBuilder node = item.Parent is null ? root : new NodeBuilder(nonTerminal);
            node.ProductionId = production.Id;
            item.Parent?.Children.Add(new BuilderPlaceholder(node));
            for (int i = production.Right.Count - 1; i >= 0; i--)
            {
                GrammarSymbol symbol = production.Right[i];
                stack.Push(symbol switch
                {
                    GrammarSymbol.Terminal t => WorkItem.Match(t.RuleName, node),
                    GrammarSymbol.NonTerminal nt => WorkItem.Expand(nt.Name, node),
                    _ => throw new InvalidOperationException("Unknown grammar symbol."),
                });
            }
        }

        if (pos < tokens.Count)
            throw Error(text, tokens, pos, new[] { EndOfInput });

        return (ParseNode.NonTerminal)root.Freeze();
    }

    private static ParserParseException Error(string text, IReadOnlyList<LexToken> tokens, int pos, IReadOnlyList<string> expected)
    {
        var sorted = expected.OrderBy(e => e, StringComparer.Ordinal).ToArray();
        string expectedText = string.Join(", ", sorted);
        if (pos < tokens.Count)
        {
            LexToken actual = tokens[pos];
            return new ParserParseException(
                $"Unexpected token '{actual.Text}' ({actual.Name}) at offset {actual.Offset} (line {actual.Line}, column {actual.Column}); expected: {expectedText}.",
                actual, sorted, actual.Offset, actual.Line, actual.Column);
        }
        // Missing input: report EOF at the end of the text, including any trailing skipped text.
        var (offset, line, column) = EndPosition(text);
        return new ParserParseException(
            $"Unexpected end of input at offset {offset} (line {line}, column {column}); expected: {expectedText}.",
            null, sorted, offset, line, column);
    }

    private static (int Offset, int Line, int Column) EndPosition(string text)
    {
        int line = 1, column = 1;
        foreach (char c in text)
        {
            if (c == '\n') { line++; column = 1; }
            else column++;
        }
        return (text.Length, line, column);
    }

    private readonly struct WorkItem
    {
        public string? TerminalRuleName { get; }
        public string? NonTerminal { get; }
        public NodeBuilder? Parent { get; }

        private WorkItem(string? terminal, string? nonTerminal, NodeBuilder? parent)
        {
            TerminalRuleName = terminal;
            NonTerminal = nonTerminal;
            Parent = parent;
        }

        public static WorkItem Match(string terminal, NodeBuilder parent) => new(terminal, null, parent);
        public static WorkItem Expand(string nonTerminal, NodeBuilder? parent) => new(null, nonTerminal, parent);
    }

    private sealed class NodeBuilder
    {
        public readonly string Name;
        public string? ProductionId;
        public readonly List<ParseNode> Children = new();

        public NodeBuilder(string name) => Name = name;

        public ParseNode Freeze()
        {
            // Children are already frozen ParseNodes except nested builders added as placeholders;
            // builders are frozen lazily here via the wrapper below.
            var frozen = new ParseNode[Children.Count];
            for (int i = 0; i < Children.Count; i++)
                frozen[i] = Children[i] is BuilderPlaceholder bp ? bp.Builder.Freeze() : Children[i];
            return new ParseNode.NonTerminal(Name, ProductionId!, frozen);
        }
    }

    // Builders are reference types added to parent child lists before their own
    // children exist; Freeze unwraps them. To keep the list typed as ParseNode we
    // wrap builders in a placeholder node.
    private sealed record BuilderPlaceholder(NodeBuilder Builder) : ParseNode;
}

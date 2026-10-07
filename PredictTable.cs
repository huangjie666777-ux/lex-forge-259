namespace LexForge259;

/// <summary>
/// The LL(1) prediction table: per non-terminal, a map from lookahead terminal
/// (lexer rule name) to the production to apply, plus a dedicated EOF column.
/// Construction fails on the first conflict; nothing is chosen arbitrarily.
/// </summary>
internal sealed class PredictTable
{
    private readonly Dictionary<string, Row> _rows;

    private sealed class Row
    {
        public readonly Dictionary<string, Production> ByTerminal = new(StringComparer.Ordinal);
        public Production? OnEof;
    }

    private PredictTable(Dictionary<string, Row> rows) => _rows = rows;

    public static PredictTable Build(GrammarModel model)
    {
        var rows = new Dictionary<string, Row>(StringComparer.Ordinal);
        foreach (string nt in model.ByLeft.Keys)
            rows[nt] = new Row();

        foreach (Production p in model.Productions)
        {
            Row row = rows[p.Left];
            HashSet<string> first = model.FirstOf(p.Right, out bool nullable);
            foreach (string terminal in first)
                Add(model, row, p.Left, terminal, p);
            if (nullable)
                foreach (string terminal in model.Follow[p.Left])
                    Add(model, row, p.Left, terminal, p);
        }
        return new PredictTable(rows);
    }

    private static void Add(GrammarModel model, Row row, string nonTerminal, string terminal, Production p)
    {
        Production? existing = terminal == Parser.EndOfInput
            ? row.OnEof
            : row.ByTerminal.GetValueOrDefault(terminal);
        if (existing is not null)
        {
            var conflicting = new[] { existing.Id, p.Id }.OrderBy(id => id, StringComparer.Ordinal).ToArray();
            throw new ParserCompileException(
                $"LL(1) conflict for non-terminal '{nonTerminal}' on lookahead '{terminal}': productions '{conflicting[0]}' and '{conflicting[1]}' compete.",
                nonTerminal, terminal, conflicting);
        }
        if (terminal == Parser.EndOfInput)
            row.OnEof = p;
        else
            row.ByTerminal[terminal] = p;
    }

    /// <summary>The production for (nonTerminal, lookahead), or null when the cell is empty. Null lookahead means EOF.</summary>
    public Production? Lookup(string nonTerminal, string? lookahead) =>
        lookahead is null ? _rows[nonTerminal].OnEof : _rows[nonTerminal].ByTerminal.GetValueOrDefault(lookahead);

    /// <summary>All terminals with a table entry for the non-terminal, sorted ordinally (EOF last is not special-cased; 'EOF' sorts by ordinal).</summary>
    public IReadOnlyList<string> ExpectedFor(string nonTerminal)
    {
        Row row = _rows[nonTerminal];
        IEnumerable<string> expected = row.ByTerminal.Keys;
        if (row.OnEof is not null)
            expected = expected.Append(Parser.EndOfInput);
        return expected.OrderBy(t => t, StringComparer.Ordinal).ToArray();
    }
}


using LexForge259;

var rules = new List<LexRule>
{
    new("Keyword", "if|else|while|return"),
    new("Ident", "[a-zA-Z_][a-zA-Z0-9_]*"),
    new("Number", "[0-9]+(\\.[0-9]+)?"),
    new("Op", "\\+|\\-|\\*|/|==|="),
    new("LParen", "\\("),
    new("RParen", "\\)"),
    new("Semicolon", ";"),
    new("Whitespace", "[ \t\r\n]+", Skip: true),
};

Lexer lexer = LexerCompiler.Compile(rules);

const string source = "if (count == 42) return total + 3.5;\nelse whilex = 0;";
Console.WriteLine($"Source: {source}");
Console.WriteLine();
foreach (LexToken t in lexer.Scan(source))
    Console.WriteLine($"{t.Name,-10} '{t.Text}'  offset={t.Offset} len={t.Length} line={t.Line} col={t.Column}");

Console.WriteLine();
try
{
    lexer.Scan("ok := 1");
}
catch (LexerScanException ex)
{
    Console.WriteLine($"Scan error: {ex.Message}");
}

// ---------------------------------------------------------------------------
// Rule audit: intersections, union shadowing, and winning witnesses
// ---------------------------------------------------------------------------
var auditRules = new List<LexRule>
{
    new("If", "if"),                 // overlaps Ident on "if", but wins ties
    new("Ident", "[a-z]+"),          // can still win on other words
    new("X1", "x1"),
    new("Y2", "y2"),
    new("Both", "x1|y2"),            // covered only by the UNION of X1 and Y2
    new("Num", "[0-9]+"),
    new("Ws", "[ ]+", Skip: true),
};

Lexer audited = LexerCompiler.Compile(auditRules);
RuleAuditReport report = audited.Audit();

Console.WriteLine();
Console.WriteLine("Rule audit");
Console.WriteLine("----------");
Console.WriteLine("Overlapping rule pairs (shared complete words):");
foreach (RuleOverlap overlap in report.Overlaps)
    Console.WriteLine($"  {overlap.FirstRule} <-> {overlap.SecondRule}  e.g. \"{overlap.Witness}\"");

Console.WriteLine();
Console.WriteLine("Per-rule reachability:");
foreach (RuleAuditEntry entry in report.Entries)
{
    if (entry.CanWin)
        Console.WriteLine($"  [{entry.Index}] {entry.RuleName,-6} can win   witness \"{entry.WinningWitness}\"");
    else
        Console.WriteLine($"  [{entry.Index}] {entry.RuleName,-6} SHADOWED  shortest \"{entry.ShortestAccepted}\" won by {entry.WinningRule}");
}

// ---------------------------------------------------------------------------
// LL(1) parser: structured grammar over the existing lexer
// ---------------------------------------------------------------------------
var scriptRules = new List<LexRule>
{
    new("KwIf", "if"),
    new("Ident", "[a-z][a-z0-9]*"),
    new("Number", "[0-9]+"),
    new("Assign", "="),
    new("Semicolon", ";"),
    new("Ws", "[ \t\n]+", Skip: true),
};

var scriptProductions = new[]
{
    new Production("list-cons", "StmtList", new GrammarSymbol[]
    {
        new GrammarSymbol.NonTerminal("Stmt"),
        new GrammarSymbol.NonTerminal("StmtList"),
    }),
    new Production("list-eps", "StmtList", Array.Empty<GrammarSymbol>()), // nullable branch
    new Production("stmt-assign", "Stmt", new GrammarSymbol[]
    {
        new GrammarSymbol.Terminal("Ident"),
        new GrammarSymbol.Terminal("Assign"),
        new GrammarSymbol.Terminal("Number"),
        new GrammarSymbol.Terminal("Semicolon"),
    }),
    new Production("stmt-if", "Stmt", new GrammarSymbol[]
    {
        new GrammarSymbol.Terminal("KwIf"),
        new GrammarSymbol.Terminal("Ident"),
        new GrammarSymbol.Terminal("Semicolon"),
    }),
};

Lexer scriptLexer = LexerCompiler.Compile(scriptRules);
Parser parser = ParserCompiler.Compile(scriptLexer, "StmtList", scriptProductions);

static void PrintTree(ParseNode node, string indent)
{
    switch (node)
    {
        case ParseNode.Terminal t:
            Console.WriteLine($"{indent}{t.Token.Name} '{t.Token.Text}' @({t.Token.Line},{t.Token.Column})");
            break;
        case ParseNode.NonTerminal nt:
            Console.WriteLine($"{indent}{nt.Name} [{nt.ProductionId}]");
            foreach (ParseNode child in nt.Children)
                PrintTree(child, indent + "  ");
            break;
    }
}

Console.WriteLine();
Console.WriteLine("Parse of a valid script: \"x = 1; if y;\"");
PrintTree(parser.Parse("x = 1; if y;"), "  ");

Console.WriteLine();
Console.WriteLine("Parse of empty input (epsilon branch):");
PrintTree(parser.Parse(""), "  ");

Console.WriteLine();
try
{
    parser.Parse("x = ;");
}
catch (ParserParseException ex)
{
    Console.WriteLine($"Syntax error: {ex.Message}");
}

try
{
    parser.Parse("x = 1 \n "); // missing ';' -> EOF error past the trailing skipped text
}
catch (ParserParseException ex)
{
    Console.WriteLine($"Syntax error: {ex.Message}");
}

// A grammar conflict fails compilation; nothing is chosen arbitrarily.
var conflictProductions = new[]
{
    new Production("a-short", "A", new GrammarSymbol[] { new GrammarSymbol.Terminal("Ident") }),
    new Production("a-long", "A", new GrammarSymbol[]
    {
        new GrammarSymbol.Terminal("Ident"),
        new GrammarSymbol.Terminal("Number"),
    }),
};
try
{
    ParserCompiler.Compile(scriptLexer, "A", conflictProductions);
}
catch (ParserCompileException ex)
{
    Console.WriteLine($"Grammar conflict: {ex.Message}");
    Console.WriteLine($"  non-terminal={ex.NonTerminal} lookahead={ex.Lookahead} productions=[{string.Join(", ", ex.ConflictingProductions!)}]");
}

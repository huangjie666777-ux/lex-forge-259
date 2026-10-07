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

# LexForge259

A lexer-rule compilation library for .NET 8 / C# 12. Rules are compiled once into a shared DFA and the resulting lexer can scan any number of texts repeatedly. No `System.Text.RegularExpressions` and no external generator: patterns are parsed by a hand-written recursive-descent parser, compiled to a Thompson NFA, then determinized via subset construction with epsilon closures.

## Public API

```csharp
var lexer = LexerCompiler.Compile(new LexRule[]
{
    new("Keyword", "if|else|while"),
    new("Ident",   "[a-zA-Z_][a-zA-Z0-9_]*"),
    new("Number",  "[0-9]+(\\.[0-9]+)?"),
    new("Ws",      "[ \\t\\n]+", Skip: true),
});
IReadOnlyList<LexToken> tokens = lexer.Scan("if x1\n  42");
```

- `LexRule(Name, Pattern, Skip)` - rules are matched in list order (priority).
- `LexToken(Name, Text, Offset, Length, Line, Column)` - offset/length are 0-based, line/column are 1-based; only LF advances the line.
- `Lexer.Scan(text)` - stateless; safe to reuse across texts and threads. Empty text returns an empty result.
- `LexerCompileException` - carries `RuleName` and `PatternOffset` for syntax errors.
- `LexerScanException` - carries `Offset`, `Line`, `Column` of the first unrecognized position; scanning stops (no character skipping).

## Rule audit

`Lexer.Audit()` reuses the compiled DFA to report how the rules compete. It is a pure computation: call it any number of times, interleave it with scans, and nothing is mutated.

```csharp
RuleAuditReport report = lexer.Audit();

foreach (RuleOverlap overlap in report.Overlaps)
    Console.WriteLine($"{overlap.FirstRule} <-> {overlap.SecondRule}: \"{overlap.Witness}\"");

foreach (RuleAuditEntry entry in report.Entries)
    // entry.CanWin ? entry.WinningWitness : entry.ShortestAccepted / entry.WinningRule
    ;
```

- `Overlaps` - every unordered pair of rules whose languages share a complete non-empty word, each pair reported once, in declaration order (`(0,1)`, `(0,2)`, ..., `(1,2)`, ...). `Witness` is a word both rules accept in full; sharing only a prefix is not an intersection. The witness is the shortest such word, and the ASCII-lexicographically smallest among equal lengths.
- `Entries[i].CanWin` - whether rule `i` can ever win from the scan start, considering the union of all earlier rules (including skipped rules). When true, `WinningWitness` is the shortest input (ASCII-lex-min tie break) for which this rule is the actual longest-match/order winner.
- When `CanWin` is false the rule is **fully shadowed**: `ShortestAccepted` is its shortest accepted word and `WinningRule` names the earlier rule that actually wins on that word. Shadowing is decided against the union of all predecessors - e.g. `x1`, `y2` together fully shadow `x1|y2` even though neither predecessor alone covers it.
- All answers come from exact automaton reachability: a breadth-first search over the compiled DFA with edges taken in ASCII order (the first path reaching a state is its shortest, lex-smallest word). No `Regex`, random sampling, or bounded word-length enumeration is involved.

## LL(1) parsing

`ParserCompiler` builds a table-driven LL(1) parser on top of an existing lexer. The caller supplies the start non-terminal and an ordered list of structured productions - no grammar strings are parsed, and no external generator is used.

```csharp
Lexer lexer = LexerCompiler.Compile(new LexRule[]
{
    new("KwIf", "if"),
    new("Ident", "[a-z][a-z0-9]*"),
    new("Number", "[0-9]+"),
    new("Assign", "="),
    new("Semicolon", ";"),
    new("Ws", "[ \t\n]+", Skip: true),
});

Parser parser = ParserCompiler.Compile(lexer, "StmtList", new Production[]
{
    new("list-cons", "StmtList", new GrammarSymbol[]
    {
        new GrammarSymbol.NonTerminal("Stmt"),
        new GrammarSymbol.NonTerminal("StmtList"),
    }),
    new("list-eps", "StmtList", Array.Empty<GrammarSymbol>()), // epsilon
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
});

ParseNode.NonTerminal tree = parser.Parse("x = 1; if y;");
```

Grammar model:

- `Production(Id, Left, Right)` - `Id` is unique across the grammar, `Left` is the left-hand non-terminal, `Right` is the ordered symbol sequence; an empty sequence is an epsilon production.
- `GrammarSymbol.Terminal(RuleName)` references a non-skip lexer rule by name; `GrammarSymbol.NonTerminal(Name)` references a non-terminal. The two are always explicitly distinguished.
- Compilation rejects: duplicate production ids, terminals with no matching lexer rule, references to skipped rules, non-terminals with no production, a start symbol with no production, and direct or indirect left recursion (including recursion hidden behind nullable prefixes).

Sets and table:

- nullable, FIRST, and FOLLOW are computed as fixed points at compile time; nullable prefixes propagate fully. FOLLOW of the start symbol contains the EOF marker.
- The prediction table has one row per non-terminal and one column per terminal plus EOF. If two productions ever compete for one cell, compilation fails with `ParserCompileException` carrying `NonTerminal`, `Lookahead`, and `ConflictingProductions` - nothing is chosen arbitrarily.

Parsing:

- `Parser.Parse(text)` reuses the bound lexer's `Scan`; lexer errors surface unchanged as `LexerScanException`. The whole token stream must be consumed - a legal prefix is never reported as success.
- On success it returns the ordered parse tree: `ParseNode.NonTerminal(Name, ProductionId, Children)` marks each expansion with its production id (epsilon nodes have no children), and `ParseNode.Terminal(Token)` preserves the original `LexToken`.
- Parsing stops at the first syntax error: `ParserParseException` carries the actual token (`Actual`, `null` at end of input), the ordinally sorted expected terminal set (`Expected`, containing `Parser.EndOfInput` = `"EOF"` when applicable), and 0-based offset plus 1-based line/column. At EOF the position is the end of the text including trailing skipped text; only LF advances the line.
- Compilation snapshots the grammar; the parser is immutable and repeated parses share no state.

## Pattern syntax

- ASCII literals and escapes: `\n`, `\r`, `\t`, and escaped metacharacters (`\\ . * + ? ( ) [ ] | ^ $ { } -`).
- Character classes with ascending ranges: `[a-z0-9_]`.
- Grouping `(...)`, concatenation, alternation `|`, repetition `*` `+` `?`.
- Precedence (high to low): repetition, concatenation, alternation.
- Not supported (compile-time error with rule name and pattern offset): wildcard `.`, negated classes `[^...]`, anchors `^` `$`, counted repetition `{m,n}`, backreferences.
- Repetition operators may be applied to subexpressions that themselves can match empty text: patterns such as `(a?)*b` compile (the mandatory `b` keeps the overall language non-empty). Only the rule pattern as a whole must reject the empty string; patterns such as `(a?)*` or `(a|b)?` are still rejected.

## Matching semantics

- Longest match wins at the current position; ties break by rule order.
- On a dead end or end of input the scanner falls back to the last accepting position; remaining characters take part in the next token, nothing is lost.
- `Skip` rules produce no token but still advance position/line/column.
- Non-ASCII input is an error reported at the first offending position.

## Limits

- NFA and DFA are each capped at 4096 states; exceeding either fails compilation with a clear error (no partial lexer is produced).
- Compilation snapshots the rule list; later caller mutations have no effect.

## Layout

- `Abstractions.cs` - public types (`LexRule`, `LexToken`, exceptions).
- `Grammar.cs` - public parser types (`GrammarSymbol`, `Production`, `ParseNode`, exceptions).
- `PatternParser.cs` / `Ast.cs` - pattern syntax tree and validation.
- `Nfa.cs` - Thompson construction.
- `Dfa.cs` - subset construction with epsilon closure, per-state accept sets and accept priorities.
- `RuleAudit.cs` - exact DFA reachability analysis (intersections, winning witnesses, union shadowing).
- `GrammarAnalysis.cs` - grammar validation and nullable/FIRST/FOLLOW fixed points, left-recursion rejection.
- `PredictTable.cs` - LL(1) prediction table with EOF column and conflict detection.
- `Parser.cs` - `ParserCompiler`, the table-driven parse loop, and tree construction.
- `Lexer.cs` - `LexerCompiler`, the scanning loop, and `Lexer.Audit()`.
- `LexForge259.Tests` - xUnit tests; `LexForge259.Demo` - runnable example.

## Build, test, demo

```sh
dotnet build LexForge259.csproj
dotnet test LexForge259.Tests
dotnet run --project LexForge259.Demo
```

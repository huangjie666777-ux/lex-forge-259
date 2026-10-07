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
- `PatternParser.cs` / `Ast.cs` - pattern syntax tree and validation.
- `Nfa.cs` - Thompson construction.
- `Dfa.cs` - subset construction with epsilon closure, per-state accept sets and accept priorities.
- `RuleAudit.cs` - exact DFA reachability analysis (intersections, winning witnesses, union shadowing).
- `Lexer.cs` - `LexerCompiler`, the scanning loop, and `Lexer.Audit()`.
- `LexForge259.Tests` - xUnit tests; `LexForge259.Demo` - runnable example.

## Build, test, demo

```sh
dotnet build LexForge259.csproj
dotnet test LexForge259.Tests
dotnet run --project LexForge259.Demo
```

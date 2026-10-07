namespace LexForge259;

/// <summary>A validated grammar snapshot with precomputed nullable/FIRST/FOLLOW sets.</summary>
internal sealed class GrammarModel
{
    public required string Start { get; init; }
    public required Production[] Productions { get; init; }
    public required Dictionary<string, List<Production>> ByLeft { get; init; }
    public required HashSet<string> Nullable { get; init; }
    public required Dictionary<string, HashSet<string>> First { get; init; }
    public required Dictionary<string, HashSet<string>> Follow { get; init; }

    /// <summary>FIRST of a symbol sequence; <paramref name="nullable"/> reports whether the whole sequence can vanish.</summary>
    public HashSet<string> FirstOf(IReadOnlyList<GrammarSymbol> sequence, out bool nullable)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        nullable = true;
        foreach (GrammarSymbol symbol in sequence)
        {
            switch (symbol)
            {
                case GrammarSymbol.Terminal t:
                    result.Add(t.RuleName);
                    nullable = false;
                    return result;
                case GrammarSymbol.NonTerminal nt:
                    result.UnionWith(First[nt.Name]);
                    if (!Nullable.Contains(nt.Name))
                    {
                        nullable = false;
                        return result;
                    }
                    break;
            }
        }
        return result;
    }
}

/// <summary>
/// Grammar validation plus nullable/FIRST/FOLLOW fixed-point computation.
/// No grammar strings are parsed; callers supply structured productions.
/// </summary>
internal static class GrammarAnalysis
{
    public static GrammarModel Analyze(string start, IReadOnlyList<Production> productions, Lexer lexer)
    {
        if (string.IsNullOrEmpty(start))
            throw new ParserCompileException("The start non-terminal must be a non-empty name.");
        ArgumentNullException.ThrowIfNull(productions);

        // Snapshot so later caller mutations cannot affect the compiled parser.
        var snapshot = productions.ToArray();
        if (snapshot.Length == 0)
            throw new ParserCompileException("At least one production is required.");

        var byLeft = new Dictionary<string, List<Production>>(StringComparer.Ordinal);
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < snapshot.Length; i++)
        {
            Production p = snapshot[i] ?? throw new ParserCompileException($"Production at index {i} is null.");
            if (string.IsNullOrEmpty(p.Id))
                throw new ParserCompileException($"Production at index {i} has an empty id.");
            if (!seenIds.Add(p.Id))
                throw new ParserCompileException($"Duplicate production id '{p.Id}'.");
            if (string.IsNullOrEmpty(p.Left))
                throw new ParserCompileException($"Production '{p.Id}' has an empty left-hand side.");
            if (p.Right is null)
                throw new ParserCompileException($"Production '{p.Id}' has a null right-hand side.");
            if (!byLeft.TryGetValue(p.Left, out List<Production>? list))
                byLeft[p.Left] = list = new List<Production>();
            list.Add(p);
        }

        if (!byLeft.ContainsKey(start))
            throw new ParserCompileException($"Start non-terminal '{start}' has no production.");

        // Reference validation: terminals must name non-skip lexer rules,
        // non-terminals must have at least one production.
        foreach (Production p in snapshot)
        {
            foreach (GrammarSymbol symbol in p.Right)
            {
                switch (symbol)
                {
                    case null:
                        throw new ParserCompileException($"Production '{p.Id}' contains a null symbol.");
                    case GrammarSymbol.Terminal t:
                        if (!lexer.TryGetRuleIndex(t.RuleName, out int ruleIndex))
                            throw new ParserCompileException($"Production '{p.Id}' references undefined terminal '{t.RuleName}' (no such lexer rule).");
                        if (lexer.IsSkipRule(ruleIndex))
                            throw new ParserCompileException($"Production '{p.Id}' references skipped lexer rule '{t.RuleName}'.");
                        break;
                    case GrammarSymbol.NonTerminal nt:
                        if (!byLeft.ContainsKey(nt.Name))
                            throw new ParserCompileException($"Production '{p.Id}' references undefined non-terminal '{nt.Name}'.");
                        break;
                }
            }
        }

        var nullable = ComputeNullable(snapshot);
        RejectLeftRecursion(snapshot, nullable);
        var first = ComputeFirst(snapshot, nullable);
        var follow = ComputeFollow(snapshot, nullable, first, start);

        return new GrammarModel
        {
            Start = start,
            Productions = snapshot,
            ByLeft = byLeft,
            Nullable = nullable,
            First = first,
            Follow = follow,
        };
    }

    private static HashSet<string> ComputeNullable(Production[] productions)
    {
        var nullable = new HashSet<string>(StringComparer.Ordinal);
        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (Production p in productions)
            {
                if (nullable.Contains(p.Left))
                    continue;
                // Epsilon production (empty right side) or all symbols nullable.
                if (p.Right.All(s => s is GrammarSymbol.NonTerminal nt && nullable.Contains(nt.Name)))
                    changed |= nullable.Add(p.Left);
            }
        }
        return nullable;
    }

    private static Dictionary<string, HashSet<string>> ComputeFirst(Production[] productions, HashSet<string> nullable)
    {
        var first = productions.Select(p => p.Left).Distinct(StringComparer.Ordinal)
            .ToDictionary(nt => nt, _ => new HashSet<string>(StringComparer.Ordinal), StringComparer.Ordinal);
        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (Production p in productions)
            {
                HashSet<string> target = first[p.Left];
                foreach (GrammarSymbol symbol in p.Right)
                {
                    if (symbol is GrammarSymbol.Terminal t)
                    {
                        changed |= target.Add(t.RuleName);
                        break; // a terminal is never nullable
                    }
                    var nt = (GrammarSymbol.NonTerminal)symbol;
                    changed |= UnionInto(target, first[nt.Name]);
                    if (!nullable.Contains(nt.Name))
                        break;
                }
            }
        }
        return first;
    }

    private static Dictionary<string, HashSet<string>> ComputeFollow(
        Production[] productions, HashSet<string> nullable, Dictionary<string, HashSet<string>> first, string start)
    {
        var follow = first.Keys.ToDictionary(nt => nt, _ => new HashSet<string>(StringComparer.Ordinal), StringComparer.Ordinal);
        follow[start].Add(Parser.EndOfInput);
        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (Production p in productions)
            {
                // Trailer accumulates what can follow the remainder; start with FOLLOW(Left).
                var trailer = new HashSet<string>(follow[p.Left], StringComparer.Ordinal);
                for (int i = p.Right.Count - 1; i >= 0; i--)
                {
                    GrammarSymbol symbol = p.Right[i];
                    if (symbol is GrammarSymbol.Terminal t)
                    {
                        trailer.Clear();
                        trailer.Add(t.RuleName);
                        continue;
                    }
                    var nt = (GrammarSymbol.NonTerminal)symbol;
                    changed |= UnionInto(follow[nt.Name], trailer);
                    if (nullable.Contains(nt.Name))
                        trailer.UnionWith(first[nt.Name]); // nullable prefix: keep propagating
                    else
                    {
                        trailer.Clear();
                        trailer.UnionWith(first[nt.Name]);
                    }
                }
            }
        }
        return follow;
    }

    /// <summary>Rejects direct and indirect left recursion (A =&gt;+ A ...) via the nullable-aware left-corner relation.</summary>
    private static void RejectLeftRecursion(Production[] productions, HashSet<string> nullable)
    {
        var leftCorners = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (Production p in productions)
        {
            foreach (GrammarSymbol symbol in p.Right)
            {
                if (symbol is GrammarSymbol.Terminal)
                    break; // a leading terminal stops left-corner propagation
                var nt = (GrammarSymbol.NonTerminal)symbol;
                if (!leftCorners.TryGetValue(p.Left, out List<string>? edges))
                    leftCorners[p.Left] = edges = new List<string>();
                if (!edges.Contains(nt.Name))
                    edges.Add(nt.Name);
                if (!nullable.Contains(nt.Name))
                    break;
            }
        }

        var state = new Dictionary<string, int>(StringComparer.Ordinal); // 0=unvisited, 1=in stack, 2=done
        var stack = new List<string>();
        foreach (string nt in leftCorners.Keys.OrderBy(n => n, StringComparer.Ordinal))
            Visit(nt);

        void Visit(string nt)
        {
            if (state.TryGetValue(nt, out int s))
            {
                if (s == 1)
                {
                    int cycleStart = stack.IndexOf(nt);
                    string cycle = string.Join(" -> ", stack.Skip(cycleStart).Append(nt));
                    throw new ParserCompileException($"Left recursion detected: {cycle}.");
                }
                return;
            }
            state[nt] = 1;
            stack.Add(nt);
            if (leftCorners.TryGetValue(nt, out List<string>? edges))
                foreach (string next in edges)
                    Visit(next);
            stack.RemoveAt(stack.Count - 1);
            state[nt] = 2;
        }
    }

    private static bool UnionInto(HashSet<string> target, HashSet<string> source)
    {
        bool changed = false;
        foreach (string item in source)
            changed |= target.Add(item);
        return changed;
    }
}


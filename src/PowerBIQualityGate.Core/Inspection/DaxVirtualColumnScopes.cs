namespace PowerBIQualityGate.Core.Inspection;

/// <summary>
/// Conservative, lexical evidence that an unqualified [Name] occurs in the
/// row context of a virtual table column created by ADDCOLUMNS or SELECTCOLUMNS.
/// This is deliberately NOT a DAX parser or semantic type checker. Returning no
/// bindings is always safe: the dependency inspector keeps its old candidates.
/// </summary>
internal static class DaxVirtualColumnScopes
{
    private const int MaxExpressionLength = 65536;
    private const int MaxTokens = 8192;
    private const int MaxDepth = 32;
    private const int MaxArguments = 128;
    private const int MaxAliases = 128;

    private enum Kind { Word, String, QuotedTable, Bracket, Open, Close, Comma, Semicolon, Equals, Other }
    private readonly record struct Token(Kind Kind, string Text, int Offset);
    private readonly record struct Range(int Start, int End);

    public static IReadOnlySet<int> FindScopedReferences(string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression) || expression.Length > MaxExpressionLength ||
            !TryTokenize(expression, out var tokens, out var pairs))
            return new HashSet<int>();

        var analysis = new Analyzer(tokens, pairs);
        return analysis.Run();
    }

    private static bool TryTokenize(string expression, out List<Token> tokens, out int[] pairs)
    {
        tokens = [];
        pairs = [];
        var stack = new Stack<int>();
        var source = expression.AsSpan();
        for (var pos = 0; pos < source.Length;)
        {
            if (char.IsWhiteSpace(source[pos])) { pos++; continue; }
            if (pos + 1 < source.Length &&
                ((source[pos] == '/' && source[pos + 1] == '/') ||
                 (source[pos] == '-' && source[pos + 1] == '-')))
            {
                pos += 2;
                while (pos < source.Length && source[pos] is not '\r' and not '\n') pos++;
                continue;
            }
            if (pos + 1 < source.Length && source[pos] == '/' && source[pos + 1] == '*')
            {
                pos += 2;
                var found = false;
                while (pos + 1 < source.Length)
                {
                    if (source[pos] == '*' && source[pos + 1] == '/')
                    {
                        pos += 2;
                        found = true;
                        break;
                    }
                    pos++;
                }
                if (!found) return false;
                continue;
            }

            var begin = pos;
            var ch = source[pos];
            if (ch is '"' or '\'' or '[')
            {
                var endChar = ch == '[' ? ']' : ch;
                var builder = new System.Text.StringBuilder();
                pos++;
                var closed = false;
                while (pos < source.Length)
                {
                    var current = source[pos++];
                    if (current == endChar)
                    {
                        if (pos < source.Length && source[pos] == endChar)
                        {
                            builder.Append(endChar);
                            pos++;
                            continue;
                        }
                        closed = true;
                        break;
                    }
                    builder.Append(current);
                }
                if (!closed) return false;
                tokens.Add(new Token(ch == '"' ? Kind.String : ch == '\'' ? Kind.QuotedTable : Kind.Bracket,
                    builder.ToString(), begin));
            }
            else if (char.IsLetter(ch) || ch == '_')
            {
                pos++;
                while (pos < source.Length && (char.IsLetterOrDigit(source[pos]) || source[pos] == '_')) pos++;
                tokens.Add(new Token(Kind.Word, source[begin..pos].ToString(), begin));
            }
            else
            {
                pos++;
                tokens.Add(new Token(ch switch
                {
                    '(' => Kind.Open,
                    ')' => Kind.Close,
                    ',' => Kind.Comma,
                    ';' => Kind.Semicolon,
                    '=' => Kind.Equals,
                    _ => Kind.Other
                }, string.Empty, begin));
            }
            if (tokens.Count > MaxTokens) return false;
        }

        pairs = Enumerable.Repeat(-1, tokens.Count).ToArray();
        for (var index = 0; index < tokens.Count; index++)
        {
            if (tokens[index].Kind == Kind.Open) stack.Push(index);
            else if (tokens[index].Kind == Kind.Close)
            {
                if (stack.Count == 0) return false;
                var opening = stack.Pop();
                pairs[opening] = index;
                pairs[index] = opening;
            }
        }
        return stack.Count == 0;
    }

    private sealed class Analyzer(List<Token> tokens, int[] pairs)
    {
        private readonly HashSet<int> _recognized = [];
        private readonly HashSet<string> _empty = new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlySet<int> Run()
        {
            var env = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            WalkBlock(0, tokens.Count, env, _empty, 0);
            return _recognized;
        }

        private static bool IsWord(Token token, string expected) =>
            token.Kind == Kind.Word && token.Text.Equals(expected, StringComparison.OrdinalIgnoreCase);

        private int FindBoundary(int from, int to)
        {
            for (var i = from; i < to; i++)
            {
                if (tokens[i].Kind == Kind.Open && pairs[i] >= 0 && pairs[i] < to)
                {
                    i = pairs[i];
                    continue;
                }
                if (IsWord(tokens[i], "VAR") || IsWord(tokens[i], "RETURN")) return i;
            }
            return to;
        }

        private void WalkBlock(int start, int end, Dictionary<string, HashSet<string>> env,
            HashSet<string> rowScope, int depth)
        {
            if (start >= end || depth >= MaxDepth) return;
            if (!IsWord(tokens[start], "VAR"))
            {
                WalkExpression(start, end, env, rowScope, depth + 1);
                return;
            }

            // A VAR symbol becomes visible only after its initializer.
            var local = new Dictionary<string, HashSet<string>>(env, StringComparer.OrdinalIgnoreCase);
            var index = start;
            while (index < end && IsWord(tokens[index], "VAR"))
            {
                if (index + 3 >= end || tokens[index + 1].Kind != Kind.Word ||
                    tokens[index + 2].Kind != Kind.Equals)
                {
                    WalkExpression(start, end, env, rowScope, depth + 1);
                    return;
                }
                var name = tokens[index + 1].Text;
                var valueStart = index + 3;
                var boundary = FindBoundary(valueStart, end);
                if (boundary >= end)
                {
                    WalkExpression(start, end, env, rowScope, depth + 1);
                    return;
                }
                WalkBlock(valueStart, boundary, local, rowScope, depth + 1);
                var aliases = ResolveTable(valueStart, boundary, local, depth + 1);
                if (aliases.Count > 0) local[name] = aliases;
                else local.Remove(name);
                index = boundary;
            }

            if (index < end && IsWord(tokens[index], "RETURN"))
                WalkBlock(index + 1, end, local, rowScope, depth + 1);
            else WalkExpression(index, end, local, rowScope, depth + 1);
        }

        private void WalkExpression(int from, int to, Dictionary<string, HashSet<string>> env,
            HashSet<string> rowScope, int depth)
        {
            if (depth >= MaxDepth) return;
            for (var i = from; i < to; i++)
            {
                if (tokens[i].Kind == Kind.Word && i + 1 < to &&
                    tokens[i + 1].Kind == Kind.Open && pairs[i + 1] < to && pairs[i + 1] > i)
                {
                    var arguments = SplitArguments(i + 1, pairs[i + 1]);
                    if (arguments is not null)
                    {
                        var function = tokens[i].Text;
                        var sourceIndex = function.Equals("TOPN", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
                        HashSet<string>? iterated = null;
                        if (arguments.Count > sourceIndex && HasRowContext(function))
                        {
                            var source = arguments[sourceIndex];
                            iterated = ResolveTable(source.Start, source.End, env, depth + 1);
                        }
                        for (var argumentIndex = 0; argumentIndex < arguments.Count; argumentIndex++)
                        {
                            var argument = arguments[argumentIndex];
                            var useIteratedScope = iterated is not null &&
                                IsRowScopedArgument(function, argumentIndex);
                            WalkBlock(argument.Start, argument.End, env,
                                useIteratedScope ? iterated! : rowScope, depth + 1);
                        }
                        i = pairs[i + 1];
                        continue;
                    }
                }

                if (tokens[i].Kind == Kind.Open && pairs[i] >= 0 && pairs[i] < to)
                {
                    WalkBlock(i + 1, pairs[i], env, rowScope, depth + 1);
                    i = pairs[i];
                }
                else if (tokens[i].Kind == Kind.Bracket && rowScope.Contains(tokens[i].Text))
                {
                    // Never suppress explicit table-qualified references.
                    var qualified = i > from && (tokens[i - 1].Kind == Kind.QuotedTable ||
                        (tokens[i - 1].Kind == Kind.Word && !IsWord(tokens[i - 1], "RETURN")));
                    if (!qualified) _recognized.Add(tokens[i].Offset);
                }
            }
        }

        private HashSet<string> ResolveTable(int from, int to,
            Dictionary<string, HashSet<string>> env, int depth)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (depth >= MaxDepth || from >= to) return result;
            while (to - from >= 2 && tokens[from].Kind == Kind.Open && pairs[from] == to - 1)
            {
                from++;
                to--;
            }
            if (to - from == 1 && tokens[from].Kind == Kind.Word)
            {
                if (env.TryGetValue(tokens[from].Text, out var found)) result.UnionWith(found);
                return result;
            }
            if (to - from < 3 || tokens[from].Kind != Kind.Word ||
                tokens[from + 1].Kind != Kind.Open || pairs[from + 1] != to - 1)
                return result;

            var fn = tokens[from].Text;
            var arguments = SplitArguments(from + 1, to - 1);
            if (arguments is null) return result;
            if (fn.Equals("ADDCOLUMNS", StringComparison.OrdinalIgnoreCase) ||
                fn.Equals("SELECTCOLUMNS", StringComparison.OrdinalIgnoreCase))
            {
                if (arguments.Count < 3 || arguments.Count % 2 == 0) return result;
                var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (var index = 1; index < arguments.Count; index += 2)
                {
                    var nameArg = arguments[index];
                    if (nameArg.End != nameArg.Start + 1 ||
                        tokens[nameArg.Start].Kind != Kind.String ||
                        tokens[nameArg.Start].Text.Length == 0)
                        return result;
                    aliases.Add(tokens[nameArg.Start].Text);
                    if (aliases.Count > MaxAliases) return result;
                }
                if (fn.Equals("ADDCOLUMNS", StringComparison.OrdinalIgnoreCase))
                {
                    var source = arguments[0];
                    result.UnionWith(ResolveTable(source.Start, source.End, env, depth + 1));
                }
                result.UnionWith(aliases);
                return result;
            }

            // These functions preserve the input table's columns.
            var sourceArg = fn.Equals("TOPN", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            if ((fn.Equals("FILTER", StringComparison.OrdinalIgnoreCase) ||
                 fn.Equals("TOPN", StringComparison.OrdinalIgnoreCase) ||
                 fn.Equals("CALCULATETABLE", StringComparison.OrdinalIgnoreCase)) &&
                arguments.Count > sourceArg)
            {
                var source = arguments[sourceArg];
                return ResolveTable(source.Start, source.End, env, depth + 1);
            }
            return result;
        }

        private List<Range>? SplitArguments(int opening, int closing)
        {
            var result = new List<Range>();
            var begin = opening + 1;
            for (var cursor = begin; cursor < closing; cursor++)
            {
                if (tokens[cursor].Kind == Kind.Open && pairs[cursor] >= 0 && pairs[cursor] < closing)
                {
                    cursor = pairs[cursor];
                    continue;
                }
                if (tokens[cursor].Kind is Kind.Comma or Kind.Semicolon)
                {
                    result.Add(new Range(begin, cursor));
                    if (result.Count > MaxArguments) return null;
                    begin = cursor + 1;
                }
            }
            result.Add(new Range(begin, closing));
            return result.Count <= MaxArguments ? result : null;
        }

        private static bool HasRowContext(string fn) =>
            fn.Equals("FILTER", StringComparison.OrdinalIgnoreCase) ||
            fn.Equals("TOPN", StringComparison.OrdinalIgnoreCase) ||
            fn.Equals("ADDCOLUMNS", StringComparison.OrdinalIgnoreCase) ||
            fn.Equals("SELECTCOLUMNS", StringComparison.OrdinalIgnoreCase) ||
            fn.Equals("SUMX", StringComparison.OrdinalIgnoreCase) ||
            fn.Equals("MAXX", StringComparison.OrdinalIgnoreCase) ||
            fn.Equals("MINX", StringComparison.OrdinalIgnoreCase) ||
            fn.Equals("AVERAGEX", StringComparison.OrdinalIgnoreCase) ||
            fn.Equals("COUNTX", StringComparison.OrdinalIgnoreCase) ||
            fn.Equals("COUNTAX", StringComparison.OrdinalIgnoreCase) ||
            fn.Equals("PRODUCTX", StringComparison.OrdinalIgnoreCase) ||
            fn.Equals("CONCATENATEX", StringComparison.OrdinalIgnoreCase) ||
            fn.Equals("RANKX", StringComparison.OrdinalIgnoreCase);

        private static bool IsRowScopedArgument(string fn, int index)
        {
            if (fn.Equals("TOPN", StringComparison.OrdinalIgnoreCase)) return index >= 2 && index % 2 == 0;
            if (fn.Equals("ADDCOLUMNS", StringComparison.OrdinalIgnoreCase) ||
                fn.Equals("SELECTCOLUMNS", StringComparison.OrdinalIgnoreCase))
                return index >= 2 && index % 2 == 0;
            if (fn.Equals("FILTER", StringComparison.OrdinalIgnoreCase)) return index == 1;
            // RANKX's optional third (value) argument is not evaluated per source row.
            return index == 1;
        }
    }
}

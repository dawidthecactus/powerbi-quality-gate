namespace PowerBIQualityGate.Core.Inspection;

/// <summary>
/// Advisory-only evidence from a deliberately narrow VAR ... RETURN shape.
/// Not a complete DAX parser, expression validator, semantic type checker or
/// substitute for a running tabular engine. Never feeds the SM-002 warning gate.
/// </summary>
public static class DaxVarReturnEvidence
{
    private const int MaxExpressionLength = 16384;
    private const int MaxVariables = 64;
    private const int MaxAliasDepth = 32;

    private readonly record struct Keyword(string Word, int Start, int End);
    private sealed record Definition(string Name, string Expression);

    public static StaticDaxTypeEvidenceKind Analyze(string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression) || expression.Length > MaxExpressionLength ||
            !TryScan(expression, out var normalized, out var keywords) ||
            keywords.Count < 2 || keywords.Count > MaxVariables + 1 ||
            !IsKeyword(keywords[0], "VAR") || !IsKeyword(keywords[^1], "RETURN") ||
            !string.IsNullOrWhiteSpace(normalized[..keywords[0].Start]))
        {
            return StaticDaxTypeEvidenceKind.Unresolved;
        }

        // A keyword must be separated from the preceding expression token.
        // For example, `1RETURN x` and `SUM(Data[Value])RETURN x` are not
        // accepted as valid declaration boundaries.
        for (var index = 1; index < keywords.Count; index++)
        {
            if (keywords[index].Start == 0 ||
                !char.IsWhiteSpace(normalized[keywords[index].Start - 1]))
            {
                return StaticDaxTypeEvidenceKind.Unresolved;
            }
        }

        var variables = new List<Definition>(keywords.Count - 1);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < keywords.Count - 1; index++)
        {
            if (!IsKeyword(keywords[index], "VAR") ||
                !TryParseDeclaration(normalized[keywords[index].End..keywords[index + 1].Start],
                    out var name, out var value) || !names.Add(name))
            {
                return StaticDaxTypeEvidenceKind.Unresolved;
            }

            variables.Add(new Definition(name, value));
        }

        var returned = normalized[keywords[^1].End..].Trim();
        if (returned.Length == 0)
        {
            return StaticDaxTypeEvidenceKind.Unresolved;
        }

        return Resolve(returned, variables, variables.Count, 0);
    }

    private static StaticDaxTypeEvidenceKind Resolve(string expression,
        IReadOnlyList<Definition> variables, int availableCount, int depth)
    {
        if (depth > MaxAliasDepth)
        {
            return StaticDaxTypeEvidenceKind.Unresolved;
        }

        // Reuse the established lexical classifier, without promoting its
        // output to a TOM-confirmed data type.
        var evidence = StaticDaxTypeEvidence.Analyze(expression);
        if (evidence != StaticDaxTypeEvidenceKind.Unresolved)
        {
            return evidence;
        }

        // Only a *bare* identifier may alias a previously defined variable.
        // Bracket references, arithmetic, wrappers and undeclared identifiers
        // remain unresolved. Forward references and cycles are not followed.
        var candidate = expression.Trim();
        if (!IsVariableName(candidate))
        {
            return StaticDaxTypeEvidenceKind.Unresolved;
        }

        for (var i = availableCount - 1; i >= 0; i--)
        {
            if (string.Equals(variables[i].Name, candidate, StringComparison.OrdinalIgnoreCase))
            {
                return Resolve(variables[i].Expression, variables, i, depth + 1);
            }
        }

        return StaticDaxTypeEvidenceKind.Unresolved;
    }

    private static bool TryParseDeclaration(string segment, out string name, out string value)
    {
        name = string.Empty;
        value = string.Empty;
        var position = 0;
        while (position < segment.Length && char.IsWhiteSpace(segment[position])) position++;
        var begin = position;
        if (position >= segment.Length || !IsWordStart(segment[position])) return false;
        position++;
        while (position < segment.Length && IsWordPart(segment[position])) position++;
        name = segment[begin..position];
        if (!IsVariableName(name)) return false;
        while (position < segment.Length && char.IsWhiteSpace(segment[position])) position++;
        if (position >= segment.Length || segment[position] != '=' ||
            (position + 1 < segment.Length && segment[position + 1] == '=')) return false;
        value = segment[(position + 1)..].Trim();
        return value.Length > 0;
    }

    // Microsoft DAX variable naming rules allow alphanumeric identifiers and
    // the double-underscore prefix. Reject unsupported spellings conservatively.
    private static bool IsVariableName(string value)
    {
        if (value.StartsWith("__", StringComparison.Ordinal))
        {
            value = value[2..];
        }

        if (value.Length == 0 || !IsAsciiLetter(value[0]) ||
            value.Skip(1).Any(c => !IsAsciiLetter(c) && !char.IsAsciiDigit(c)))
        {
            return false;
        }

        return !value.Equals("VAR", StringComparison.OrdinalIgnoreCase) &&
               !value.Equals("RETURN", StringComparison.OrdinalIgnoreCase) &&
               !value.Equals("TRUE", StringComparison.OrdinalIgnoreCase) &&
               !value.Equals("FALSE", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Mask comments with spaces and find only top-level VAR/RETURN keywords.
    /// Balanced quotes, bracket identifiers, parentheses and braces are checked.
    /// Comment text and quoted tokens cannot introduce artificial boundaries.
    /// </summary>
    private static bool TryScan(string expression, out string normalized, out List<Keyword> keywords)
    {
        normalized = string.Empty;
        keywords = [];
        var chars = expression.ToCharArray();
        var parens = 0;
        var braces = 0;
        var i = 0;
        while (i < chars.Length)
        {
            var c = chars[i];
            if (i + 1 < chars.Length &&
                ((c == '/' && chars[i + 1] == '/') || (c == '-' && chars[i + 1] == '-')))
            {
                var start = i;
                while (i < chars.Length && chars[i] is not '\r' and not '\n') i++;
                for (var k = start; k < i; k++) chars[k] = ' ';
                continue;
            }

            if (i + 1 < chars.Length && c == '/' && chars[i + 1] == '*')
            {
                var start = i;
                i += 2;
                while (i + 1 < chars.Length && !(chars[i] == '*' && chars[i + 1] == '/')) i++;
                if (i + 1 >= chars.Length) return false;
                i += 2;
                for (var k = start; k < i; k++)
                {
                    if (chars[k] is not '\r' and not '\n') chars[k] = ' ';
                }
                continue;
            }

            if (c is '"' or '\'' or '[')
            {
                if (!TrySkipQuoted(chars, ref i, c == '[' ? ']' : c)) return false;
                continue;
            }

            if (c == '(') { parens++; i++; continue; }
            if (c == ')') { if (--parens < 0) return false; i++; continue; }
            if (c == '{') { braces++; i++; continue; }
            if (c == '}') { if (--braces < 0) return false; i++; continue; }
            if (c == ']' || (char.IsControl(c) && !char.IsWhiteSpace(c))) return false;

            if (IsWordStart(c))
            {
                var start = i++;
                while (i < chars.Length && IsWordPart(chars[i])) i++;
                if (parens == 0 && braces == 0)
                {
                    var token = new string(chars, start, i - start);
                    if (token.Equals("VAR", StringComparison.OrdinalIgnoreCase) ||
                        token.Equals("RETURN", StringComparison.OrdinalIgnoreCase))
                    {
                        keywords.Add(new Keyword(token, start, i));
                    }
                }
                continue;
            }

            i++;
        }

        if (parens != 0 || braces != 0) return false;
        normalized = new string(chars);
        return true;
    }

    private static bool TrySkipQuoted(char[] chars, ref int position, char closing)
    {
        position++;
        while (position < chars.Length)
        {
            if (chars[position] == closing)
            {
                if (position + 1 < chars.Length && chars[position + 1] == closing)
                {
                    position += 2;
                    continue;
                }
                position++;
                return true;
            }
            position++;
        }
        return false;
    }

    private static bool IsKeyword(Keyword token, string word) =>
        token.Word.Equals(word, StringComparison.OrdinalIgnoreCase);

    private static bool IsWordStart(char c) => IsAsciiLetter(c) || c == '_';
    private static bool IsWordPart(char c) => IsWordStart(c) || char.IsAsciiDigit(c);
    private static bool IsAsciiLetter(char c) => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z';
}

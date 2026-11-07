namespace PowerBIQualityGate.Core.Inspection;

/// <summary>
/// Advisory-only type evidence based on a deliberately narrow DAX expression shape.
/// This is not a DAX parser, type checker or substitute for a Power BI engine.
/// </summary>
public enum StaticDaxTypeEvidenceKind
{
    NotAssessed,
    LikelyNumeric,
    LikelyNonNumeric,
    Unresolved
}

/// <summary>
/// Only complete top-level calls to a small allowlist, scalar literals, and
/// boolean literals are recognized. A lexical match is never a certified type.
/// Invalid expressions, comments, wrappers, branching, and unsupported syntax
/// deliberately remain Unresolved.
/// </summary>
public static class StaticDaxTypeEvidence
{
    private const int MaxExpressionLength = 16384;

    public static StaticDaxTypeEvidenceKind Analyze(string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression) || expression.Length > MaxExpressionLength)
        {
            return StaticDaxTypeEvidenceKind.Unresolved;
        }

        var dax = expression.Trim();
        if (IsNumericLiteral(dax))
        {
            return StaticDaxTypeEvidenceKind.LikelyNumeric;
        }

        if (IsTextLiteral(dax))
        {
            return StaticDaxTypeEvidenceKind.LikelyNonNumeric;
        }

        if (!TryGetCompleteTopLevelCall(dax, out var function, out var hasArgument))
        {
            return StaticDaxTypeEvidenceKind.Unresolved;
        }

        if (!hasArgument && (function.Equals("TRUE", StringComparison.OrdinalIgnoreCase) ||
                             function.Equals("FALSE", StringComparison.OrdinalIgnoreCase)))
        {
            return StaticDaxTypeEvidenceKind.LikelyNonNumeric;
        }

        if (!hasArgument)
        {
            return StaticDaxTypeEvidenceKind.Unresolved;
        }

        return function.ToUpperInvariant() switch
        {
            "COUNTROWS" or "COUNT" or "COUNTBLANK" or "DISTINCTCOUNT" or
            "DISTINCTCOUNTNOBLANK" or "SUM" or "AVERAGE" =>
                StaticDaxTypeEvidenceKind.LikelyNumeric,
            _ => StaticDaxTypeEvidenceKind.Unresolved
        };
    }

    // DAX uses invariant decimal dots in this narrow literal classifier.
    // We do not accept thousands separators, percent suffixes or expressions.
    private static bool IsNumericLiteral(string value)
    {
        var i = 0;
        if (i < value.Length && (value[i] is '+' or '-')) i++;
        var before = 0;
        while (i < value.Length && char.IsAsciiDigit(value[i])) { before++; i++; }
        var after = 0;
        if (i < value.Length && value[i] == '.')
        {
            i++;
            while (i < value.Length && char.IsAsciiDigit(value[i])) { after++; i++; }
        }
        if (before + after == 0) return false;
        if (i < value.Length && (value[i] is 'e' or 'E'))
        {
            i++;
            if (i < value.Length && (value[i] is '+' or '-')) i++;
            var exponent = 0;
            while (i < value.Length && char.IsAsciiDigit(value[i])) { exponent++; i++; }
            if (exponent == 0) return false;
        }
        return i == value.Length;
    }

    private static bool IsTextLiteral(string value)
    {
        if (value.Length < 2 || value[0] != '"') return false;
        for (var i = 1; i < value.Length; i++)
        {
            if (value[i] != '"') continue;
            if (i + 1 < value.Length && value[i + 1] == '"')
            {
                i++;
                continue;
            }
            return i == value.Length - 1;
        }
        return false;
    }

    private static bool TryGetCompleteTopLevelCall(string dax, out string function, out bool hasArgument)
    {
        function = string.Empty;
        hasArgument = false;
        var index = 0;
        if (dax.Length == 0 || !IsLetter(dax[index])) return false;
        while (index < dax.Length && (IsLetter(dax[index]) || char.IsAsciiDigit(dax[index]) || dax[index] == '_')) index++;
        function = dax[..index];
        while (index < dax.Length && char.IsWhiteSpace(dax[index])) index++;
        if (index >= dax.Length || dax[index] != '(') return false;
        var opening = index;
        var depth = 0;
        var doubleQuoted = false;
        var singleQuoted = false;
        var bracketed = false;

        for (var i = opening; i < dax.Length; i++)
        {
            var c = dax[i];
            if (doubleQuoted)
            {
                if (c == '"')
                {
                    if (i + 1 < dax.Length && dax[i + 1] == '"') i++;
                    else doubleQuoted = false;
                }
                continue;
            }
            if (singleQuoted)
            {
                if (c == '\'')
                {
                    if (i + 1 < dax.Length && dax[i + 1] == '\'') i++;
                    else singleQuoted = false;
                }
                continue;
            }
            if (bracketed)
            {
                if (c == ']')
                {
                    if (i + 1 < dax.Length && dax[i + 1] == ']') i++;
                    else bracketed = false;
                }
                continue;
            }

            // Comment handling is intentionally unsupported: never guess whether
            // comments alter expression boundaries or the meaning of a token.
            if (i + 1 < dax.Length &&
                ((c == '/' && (dax[i + 1] is '/' or '*')) ||
                 (c == '-' && dax[i + 1] == '-'))) return false;

            if (c == '"') { doubleQuoted = true; continue; }
            if (c == '\'') { singleQuoted = true; continue; }
            if (c == '[') { bracketed = true; continue; }
            if (c == '(') { depth++; continue; }
            if (c == ')')
            {
                depth--;
                if (depth < 0 || (depth == 0 && i != dax.Length - 1)) return false;
                continue;
            }
            if (depth == 1 && (c is ',' or ';')) return false; // allowlisted calls accept one argument
            if (char.IsControl(c) && c is not '\r' and not '\n' and not '\t') return false;
        }

        if (doubleQuoted || singleQuoted || bracketed || depth != 0) return false;
        hasArgument = !string.IsNullOrWhiteSpace(dax.Substring(opening + 1, dax.Length - opening - 2));
        return true;
    }

    private static bool IsLetter(char c) => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z';
}

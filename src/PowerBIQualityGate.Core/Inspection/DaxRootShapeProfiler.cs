namespace PowerBIQualityGate.Core.Inspection;

/// <summary>
/// A coarse, privacy-safe classification of the first lexical token of a DAX
/// measure expression. It is not a DAX parser or a result-type inference.
/// </summary>
public enum DaxRootShapeKind
{
    NotApplicable,
    Variable,
    Calculate,
    Divide,
    Conditional,
    Iterator,
    OtherFunction,
    ReferenceOrOperator,
    Other
}

public sealed record DaxRootShapeCount(DaxRootShapeKind Shape, int Count, int MissingFormat);

public static class DaxRootShapeProfiler
{
    public static DaxRootShapeKind Analyze(string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return DaxRootShapeKind.Other;
        }

        var text = expression.AsSpan().TrimStart();
        if (text.StartsWith("//", StringComparison.Ordinal) ||
            text.StartsWith("--", StringComparison.Ordinal) ||
            text.StartsWith("/*", StringComparison.Ordinal))
        {
            return DaxRootShapeKind.Other;
        }

        if (!IsAsciiLetter(text[0]))
        {
            return DaxRootShapeKind.ReferenceOrOperator;
        }

        var index = 1;
        while (index < text.Length && (IsAsciiLetter(text[index]) ||
            char.IsAsciiDigit(text[index]) || text[index] == '_'))
        {
            index++;
        }

        var token = text[..index];
        var remainder = text[index..].TrimStart();

        if (token.Equals("VAR", StringComparison.OrdinalIgnoreCase) &&
            remainder.Length > 0 && remainder[0] != '(')
        {
            return DaxRootShapeKind.Variable;
        }

        if (remainder.Length == 0 || remainder[0] != '(')
        {
            return DaxRootShapeKind.ReferenceOrOperator;
        }

        if (token.Equals("CALCULATE", StringComparison.OrdinalIgnoreCase) ||
            token.Equals("CALCULATETABLE", StringComparison.OrdinalIgnoreCase))
        {
            return DaxRootShapeKind.Calculate;
        }

        if (token.Equals("DIVIDE", StringComparison.OrdinalIgnoreCase))
        {
            return DaxRootShapeKind.Divide;
        }

        if (Matches(token, "IF", "IFERROR", "SWITCH", "COALESCE"))
        {
            return DaxRootShapeKind.Conditional;
        }

        if (Matches(token, "SUMX", "AVERAGEX", "COUNTX", "COUNTAX", "MINX", "MAXX",
                "RANKX", "PRODUCTX", "MEDIANX", "CONCATENATEX"))
        {
            return DaxRootShapeKind.Iterator;
        }

        return DaxRootShapeKind.OtherFunction;
    }

    /// <summary>Fixed labels only; no user-controlled token is ever rendered.</summary>
    public static string Label(DaxRootShapeKind kind) => kind switch
    {
        DaxRootShapeKind.Variable => "VAR / RETURN",
        DaxRootShapeKind.Calculate => "CALCULATE / CALCULATETABLE",
        DaxRootShapeKind.Divide => "DIVIDE",
        DaxRootShapeKind.Conditional => "IF / SWITCH / COALESCE",
        DaxRootShapeKind.Iterator => "X iterator",
        DaxRootShapeKind.OtherFunction => "Other function call",
        DaxRootShapeKind.ReferenceOrOperator => "Reference / operator / literal",
        DaxRootShapeKind.Other => "Other / comment-first",
        _ => "Not applicable"
    };

    private static bool Matches(ReadOnlySpan<char> value, params string[] known)
    {
        foreach (var name in known)
        {
            if (value.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsAsciiLetter(char value) =>
        value is >= 'A' and <= 'Z' or >= 'a' and <= 'z';
}

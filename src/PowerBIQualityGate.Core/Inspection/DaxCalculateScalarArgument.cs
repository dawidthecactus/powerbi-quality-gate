namespace PowerBIQualityGate.Core.Inspection;

/// <summary>
/// Conservative lexical extraction of CALCULATE's scalar-expression argument.
/// Not a DAX parser, semantic validator or type checker. Does not evaluate DAX.
/// CALCULATETABLE is excluded because its first argument is tabular.
/// </summary>
internal static class DaxCalculateScalarArgument
{
    private const int MaxLength = 16384;
    private const int MaxNesting = 64;
    private const int MaxArguments = 64;
    private const string Name = "CALCULATE";

    public static bool TryExtract(string? expression, out string scalar)
    {
        scalar = string.Empty;
        if (string.IsNullOrWhiteSpace(expression) || expression.Length > MaxLength)
            return false;

        var text = expression.Trim();
        if (!text.StartsWith(Name, StringComparison.OrdinalIgnoreCase)) return false;

        var position = Name.Length;
        // Reject CALCULATETABLE, CALCULATE_... and identifiers sharing a prefix.
        if (position < text.Length && !char.IsWhiteSpace(text[position]) && text[position] != '(')
            return false;
        while (position < text.Length && char.IsWhiteSpace(text[position])) position++;
        if (position >= text.Length || text[position] != '(' || text[^1] != ')')
            return false;

        var delimiters = new Stack<char>();
        delimiters.Push('(');
        var argumentStart = position + 1;
        var argumentCount = 0;
        var firstEnd = -1;

        for (var i = position + 1; i < text.Length; i++)
        {
            var c = text[i];
            if (c is '"' or '\'' or '[')
            {
                if (!SkipQuoted(text, ref i, c == '[' ? ']' : c)) return false;
                continue;
            }
            // The outer VAR scanner masks comments before invoking this helper.
            if (i + 1 < text.Length &&
                ((c == '/' && (text[i + 1] == '/' || text[i + 1] == '*')) ||
                 (c == '-' && text[i + 1] == '-')))
                return false;

            if (c is '(' or '{')
            {
                if (delimiters.Count >= MaxNesting) return false;
                delimiters.Push(c);
                continue;
            }
            if (c is ')' or '}')
            {
                if (delimiters.Count == 0) return false;
                var expected = c == ')' ? '(' : '{';
                if (delimiters.Pop() != expected) return false;
                if (delimiters.Count == 0)
                {
                    if (i != text.Length - 1 || !AddArgument(argumentStart, i, ref argumentCount,
                            ref firstEnd, text)) return false;
                    scalar = text[(position + 1)..firstEnd].Trim();
                    return scalar.Length > 0;
                }
                continue;
            }
            if (c == ']' || (char.IsControl(c) && !char.IsWhiteSpace(c))) return false;
            if ((c is ',' or ';') && delimiters.Count == 1)
            {
                if (!AddArgument(argumentStart, i, ref argumentCount, ref firstEnd, text)) return false;
                argumentStart = i + 1;
            }
        }
        return false;
    }

    private static bool AddArgument(int start, int end, ref int count, ref int firstEnd, string text)
    {
        if (count == MaxArguments || string.IsNullOrWhiteSpace(text[start..end])) return false;
        if (count == 0) firstEnd = end;
        count++;
        return true;
    }

    private static bool SkipQuoted(string text, ref int position, char closing)
    {
        for (var i = position + 1; i < text.Length; i++)
        {
            if (text[i] != closing) continue;
            if (i + 1 < text.Length && text[i + 1] == closing)
            {
                i++;
                continue;
            }
            position = i;
            return true;
        }
        return false;
    }
}

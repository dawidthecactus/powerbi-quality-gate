namespace PowerBIQualityGate.Core.Inspection;

/// <summary>
/// Extracts only potential RESULT expressions from complete IF/SWITCH/COALESCE
/// calls. This is bounded lexical recognition, NOT a DAX parser, validator or
/// semantic type checker. Inputs come from the comment-masking VAR scanner.
/// </summary>
internal static class DaxConditionalScalarArguments
{
    private const int MaxLength = 16384;
    private const int MaxNesting = 64;
    private const int MaxArguments = 64;

    public static bool TryExtract(string? expression, out string[] results)
    {
        results = [];
        if (string.IsNullOrWhiteSpace(expression) || expression.Length > MaxLength)
            return false;

        var text = expression.Trim();
        var position = 0;
        while (position < text.Length && IsLetter(text[position])) position++;
        if (position == 0) return false;
        var function = text[..position];
        if (!function.Equals("IF", StringComparison.OrdinalIgnoreCase) &&
            !function.Equals("SWITCH", StringComparison.OrdinalIgnoreCase) &&
            !function.Equals("COALESCE", StringComparison.OrdinalIgnoreCase))
            return false;

        while (position < text.Length && char.IsWhiteSpace(text[position])) position++;
        if (position >= text.Length || text[position] != '(' || text[^1] != ')')
            return false;

        var arguments = new List<string>();
        var delimiters = new Stack<char>();
        delimiters.Push('(');
        var start = position + 1;
        for (var i = position + 1; i < text.Length; i++)
        {
            var c = text[i];
            if (c is '"' or '\'' or '[')
            {
                if (!SkipQuoted(text, ref i, c == '[' ? ']' : c)) return false;
                continue;
            }
            // The upstream VAR tokenizer masks comments. Do not interpret
            // arbitrary unmasked comments as operators or separators.
            if (i + 1 < text.Length &&
                ((c == '/' && (text[i + 1] is '/' or '*')) ||
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
                if (delimiters.Count == 0 ||
                    delimiters.Pop() != (c == ')' ? '(' : '{')) return false;
                if (delimiters.Count == 0)
                {
                    if (i != text.Length - 1 || !AddArgument(text, start, i, arguments))
                        return false;
                    return TrySelectResults(function, arguments, out results);
                }
                continue;
            }
            if (c == ']' || (char.IsControl(c) && !char.IsWhiteSpace(c))) return false;
            if ((c is ',' or ';') && delimiters.Count == 1)
            {
                if (!AddArgument(text, start, i, arguments)) return false;
                start = i + 1;
            }
        }
        return false;
    }

    private static bool TrySelectResults(string function, List<string> args, out string[] results)
    {
        results = [];
        if (function.Equals("IF", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Count is < 2 or > 3) return false;
            results = args.Skip(1).ToArray(); // omitted false is implicit BLANK
            return true;
        }
        if (function.Equals("COALESCE", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Count < 2) return false;
            results = args.ToArray();
            return true;
        }
        if (args.Count < 3) return false;
        // SWITCH(selector, value, result [, value, result]* [, else])
        var hasElse = args.Count % 2 == 0;
        var finalPairEnd = hasElse ? args.Count - 1 : args.Count;
        var selected = new List<string>();
        for (var i = 2; i < finalPairEnd; i += 2) selected.Add(args[i]);
        if (hasElse) selected.Add(args[^1]);
        results = selected.ToArray(); // omitted else is implicit BLANK
        return results.Length > 0;
    }

    public static bool IsExplicitBlank(string expression)
    {
        var text = expression.AsSpan().Trim();
        const string name = "BLANK";
        if (!text.StartsWith(name, StringComparison.OrdinalIgnoreCase)) return false;
        text = text[name.Length..].Trim();
        if (text.Length == 0 || text[0] != '(' || text[^1] != ')') return false;
        return text[1..^1].Trim().IsEmpty;
    }

    private static bool AddArgument(string text, int begin, int end, List<string> args)
    {
        if (args.Count >= MaxArguments) return false;
        var result = text[begin..end].Trim();
        if (result.Length == 0) return false;
        args.Add(result);
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

    private static bool IsLetter(char c) => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z';
}

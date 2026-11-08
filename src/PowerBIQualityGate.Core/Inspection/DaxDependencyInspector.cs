namespace PowerBIQualityGate.Core.Inspection;

/// <summary>
/// A candidate for local review, NOT a proven DAX semantic error. These values
/// contain private object identifiers and must not be printed by default.
/// </summary>
public enum DaxDependencyCandidateKind
{
    MissingTable,
    MissingQualifiedMember,
    UnresolvedUnqualifiedReference
}

public sealed record DaxDependencyCandidate(
    string MeasureTable,
    string MeasureName,
    string? ReferenceTable,
    string ReferenceName,
    DaxDependencyCandidateKind Kind);

public sealed record DaxDependencyReport(
    int InspectedMeasures,
    int SkippedExpressions,
    IReadOnlyList<DaxDependencyCandidate> Candidates)
{
    public int AffectedMeasures => Candidates
        .Select(candidate => (candidate.MeasureTable, candidate.MeasureName))
        .Distinct().Count();

    public int Count(DaxDependencyCandidateKind kind) =>
        Candidates.Count(candidate => candidate.Kind == kind);
}

/// <summary>
/// Conservative, offline DAX reference discovery based on TOM inventory.
/// Recognizes table-qualified references and unqualified bracket identifiers.
/// It cannot resolve virtual tables, scope, calculation groups, or all DAX
/// semantics. Findings are strictly opt-in, advisory, and never gate warnings.
/// </summary>
public static class DaxDependencyInspector
{
    private const int MaxExpressionLength = 65536;
    private const int MaxReferences = 512;

    private readonly record struct Reference(string? Table, string Member);

    public static DaxDependencyReport Inspect(TomModelSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var tables = snapshot.Tables.ToDictionary(table => table.Name,
            StringComparer.OrdinalIgnoreCase);
        var allMeasureNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var allColumnNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var table in snapshot.Tables)
        {
            foreach (var measure in table.Measures) allMeasureNames.Add(measure.Name);
            foreach (var column in table.Columns) allColumnNames.Add(column.Name);
        }

        var candidates = new List<DaxDependencyCandidate>();
        var inspected = 0;
        var skipped = 0;
        foreach (var table in snapshot.Tables)
        {
            foreach (var measure in table.Measures)
            {
                inspected++;
                if (!TryReadReferences(measure.Expression, out var references))
                {
                    skipped++;
                    continue;
                }

                var perMeasure = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var reference in references)
                {
                    DaxDependencyCandidateKind kind;
                    if (reference.Table is null)
                    {
                        // Unqualified [Name] can bind to a measure, column or
                        // virtual column. Only flag names missing from the
                        // entire physical TOM inventory, for manual review.
                        if (allMeasureNames.Contains(reference.Member) ||
                            allColumnNames.Contains(reference.Member)) continue;
                        kind = DaxDependencyCandidateKind.UnresolvedUnqualifiedReference;
                    }
                    else if (!tables.TryGetValue(reference.Table, out var referencedTable))
                    {
                        kind = DaxDependencyCandidateKind.MissingTable;
                    }
                    else if (!referencedTable.Columns.Any(column =>
                                 string.Equals(column.Name, reference.Member,
                                     StringComparison.OrdinalIgnoreCase)) &&
                             !referencedTable.Measures.Any(other =>
                                 string.Equals(other.Name, reference.Member,
                                     StringComparison.OrdinalIgnoreCase)))
                    {
                        kind = DaxDependencyCandidateKind.MissingQualifiedMember;
                    }
                    else
                    {
                        continue;
                    }

                    var key = string.Join('\u001f', ((int)kind).ToString(),
                        reference.Table ?? string.Empty, reference.Member);
                    if (!perMeasure.Add(key)) continue;
                    candidates.Add(new DaxDependencyCandidate(table.Name, measure.Name,
                        reference.Table, reference.Member, kind));
                }
            }
        }

        return new DaxDependencyReport(inspected, skipped, candidates);
    }

    /// <summary>
    /// Fully skip malformed/unbalanced expressions: partial discovery is not
    /// trustworthy. Strings, comments, escaped identifiers and names of
    /// quoted tables are never mistaken for independent dependencies.
    /// </summary>
    private static bool TryReadReferences(string? expression, out List<Reference> references)
    {
        references = [];
        if (expression is null || expression.Length > MaxExpressionLength) return false;
        var text = expression.AsSpan();
        var i = 0;
        var parentheses = 0;
        var braces = 0;
        while (i < text.Length)
        {
            var current = text[i];
            if (i + 1 < text.Length && current == '/' && text[i + 1] == '/')
            {
                i += 2;
                SkipLineComment(text, ref i);
                continue;
            }
            if (i + 1 < text.Length && current == '-' && text[i + 1] == '-')
            {
                i += 2;
                SkipLineComment(text, ref i);
                continue;
            }
            if (i + 1 < text.Length && current == '/' && text[i + 1] == '*')
            {
                i += 2;
                if (!SkipBlockComment(text, ref i)) return false;
                continue;
            }
            if (current == '"')
            {
                if (!TryReadDelimited(text, ref i, '"', out _)) return false;
                continue;
            }
            if (current == '\'')
            {
                if (!TryReadDelimited(text, ref i, '\'', out var quotedTable)) return false;
                var after = i;
                SkipSpace(text, ref after);
                if (after < text.Length && text[after] == '[')
                {
                    i = after;
                    if (!TryReadBracket(text, ref i, out var member)) return false;
                    references.Add(new Reference(quotedTable, member));
                }
            }
            else if (current == '[')
            {
                if (!TryReadBracket(text, ref i, out var member)) return false;
                references.Add(new Reference(null, member));
            }
            else if (IsWordStart(current))
            {
                var start = i++;
                while (i < text.Length && IsWordPart(text[i])) i++;
                var word = text[start..i].ToString();
                var after = i;
                SkipSpace(text, ref after);
                if (after < text.Length && text[after] == '[')
                {
                    i = after;
                    if (!TryReadBracket(text, ref i, out var member)) return false;
                    // RETURN is a DAX keyword, never an unquoted table qualifier.
                    // RETURN [Measure] means an unqualified bracket reference.
                    references.Add(new Reference(
                        word.Equals("RETURN", StringComparison.OrdinalIgnoreCase)
                            ? null : word, member));
                }
            }
            else if (current == '(')
            {
                if (++parentheses > 128) return false;
                i++;
            }
            else if (current == ')')
            {
                if (--parentheses < 0) return false;
                i++;
            }
            else if (current == '{')
            {
                if (++braces > 128) return false;
                i++;
            }
            else if (current == '}')
            {
                if (--braces < 0) return false;
                i++;
            }
            else if (char.IsControl(current) && !char.IsWhiteSpace(current))
            {
                return false;
            }
            else
            {
                i++;
            }
            if (references.Count > MaxReferences) return false;
        }
        return parentheses == 0 && braces == 0;
    }

    private static void SkipSpace(ReadOnlySpan<char> text, ref int i)
    {
        while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
    }

    private static void SkipLineComment(ReadOnlySpan<char> text, ref int i)
    {
        while (i < text.Length && text[i] is not '\r' and not '\n') i++;
    }

    private static bool SkipBlockComment(ReadOnlySpan<char> text, ref int i)
    {
        while (i + 1 < text.Length)
        {
            if (text[i] == '*' && text[i + 1] == '/')
            {
                i += 2;
                return true;
            }
            i++;
        }
        return false;
    }

    private static bool TryReadDelimited(ReadOnlySpan<char> text, ref int i,
        char quote, out string content)
    {
        var value = new System.Text.StringBuilder();
        i++;
        while (i < text.Length)
        {
            var c = text[i++];
            if (c == quote)
            {
                if (i < text.Length && text[i] == quote)
                {
                    value.Append(quote);
                    i++;
                    continue;
                }
                content = value.ToString();
                return true;
            }
            value.Append(c);
        }
        content = string.Empty;
        return false;
    }

    private static bool TryReadBracket(ReadOnlySpan<char> text, ref int i,
        out string name)
    {
        var value = new System.Text.StringBuilder();
        i++;
        while (i < text.Length)
        {
            var c = text[i++];
            if (c == ']')
            {
                if (i < text.Length && text[i] == ']')
                {
                    value.Append(']');
                    i++;
                    continue;
                }
                name = value.ToString();
                return name.Length > 0;
            }
            value.Append(c);
        }
        name = string.Empty;
        return false;
    }

    private static bool IsWordStart(char c) => char.IsLetter(c) || c == '_';
    private static bool IsWordPart(char c) => char.IsLetterOrDigit(c) || c == '_';
}

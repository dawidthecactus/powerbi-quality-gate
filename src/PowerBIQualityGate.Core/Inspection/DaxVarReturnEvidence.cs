namespace PowerBIQualityGate.Core.Inspection;

/// <summary>
/// Fixed, privacy-safe reason codes for conservative VAR/RETURN analysis.
/// Reasons describe analyzer limitations, NOT DAX syntax errors or model defects.
/// </summary>
public enum DaxVarReturnDiagnosticReason
{
    NotApplicable,
    StructureRejected,
    UnsupportedReturnExpression,
    UnsupportedVariableExpression,
    UnknownOrForwardVariable,
    AnalysisLimitExceeded
}

/// <summary>
/// Fixed diagnostic categories describing why a conditional-root expression
/// was not recognized. These are analyzer limitations, never DAX errors.
/// </summary>
public enum DaxConditionalResolutionKind
{
    NotApplicable,
    UnsupportedCallShape,
    UnresolvedResultBranch,
    MixedResultTypes,
    OnlyBlankResults
}

public readonly record struct DaxVarReturnDiagnostic(
    StaticDaxTypeEvidenceKind Evidence,
    DaxVarReturnDiagnosticReason Reason,
    DaxRootShapeKind BlockedRootShape)
{
    // Zero values mean NotApplicable, preserving existing call sites.
    public DaxConditionalResolutionKind ConditionalReason { get; init; }
    public DaxRootShapeKind FirstUnresolvedConditionalBranchShape { get; init; }
}

public sealed record DaxVarReturnReasonCount(
    DaxVarReturnDiagnosticReason Reason, int Count, int MissingFormat);

public sealed record DaxConditionalResolutionCount(
    DaxConditionalResolutionKind Reason, int Count, int MissingFormat);

/// <summary>
/// Advisory-only evidence from a deliberately narrow VAR ... RETURN shape.
/// Not a complete DAX parser, expression validator, semantic type checker or
/// substitute for a running tabular engine. Never feeds the SM-002 warning gate.
/// </summary>
public static class DaxVarReturnEvidence
{
    /// <summary>Fixed labels; no private identifiers or expression text are emitted.</summary>
    public static string ReasonLabel(DaxVarReturnDiagnosticReason reason) => reason switch
    {
        DaxVarReturnDiagnosticReason.StructureRejected => "Structure / tokenizer rejected",
        DaxVarReturnDiagnosticReason.UnsupportedReturnExpression => "Unsupported RETURN expression",
        DaxVarReturnDiagnosticReason.UnsupportedVariableExpression => "Unsupported variable expression",
        DaxVarReturnDiagnosticReason.UnknownOrForwardVariable => "Unknown / forward alias",
        DaxVarReturnDiagnosticReason.AnalysisLimitExceeded => "Analysis limits exceeded",
        _ => "Not applicable"
    };

    /// <summary>Fixed-label only, safe for anonymous aggregate printing.</summary>
    public static string ConditionalLabel(DaxConditionalResolutionKind kind) => kind switch
    {
        DaxConditionalResolutionKind.UnsupportedCallShape => "Unsupported conditional call shape",
        DaxConditionalResolutionKind.UnresolvedResultBranch => "Unresolved result branch",
        DaxConditionalResolutionKind.MixedResultTypes => "Mixed numeric / nonnumeric result kinds",
        DaxConditionalResolutionKind.OnlyBlankResults => "Only BLANK results",
        _ => "Not applicable"
    };

    private const int MaxExpressionLength = 16384;
    private const int MaxVariables = 64;
    private const int MaxAliasDepth = 32;
    private const int MaxResolutionSteps = 512;

    private readonly record struct Keyword(string Word, int Start, int End);
    private sealed record Definition(string Name, string Expression);

    /// <summary>Preserves the SM-002.3b API and its exact evidence semantics.</summary>
    public static StaticDaxTypeEvidenceKind Analyze(string? expression) =>
        AnalyzeDetailed(expression).Evidence;

    /// <summary>
    /// Reports the first conservative failure reason without exposing any DAX,
    /// variable or measure identifiers. This remains advisory-only.
    /// </summary>
    public static DaxVarReturnDiagnostic AnalyzeDetailed(string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return Failure(DaxVarReturnDiagnosticReason.StructureRejected);
        }
        if (expression.Length > MaxExpressionLength)
        {
            return Failure(DaxVarReturnDiagnosticReason.AnalysisLimitExceeded);
        }
        if (!TryScan(expression, out var normalized, out var keywords))
        {
            return Failure(DaxVarReturnDiagnosticReason.StructureRejected);
        }
        if (keywords.Count > MaxVariables + 1)
        {
            return Failure(DaxVarReturnDiagnosticReason.AnalysisLimitExceeded);
        }
        if (keywords.Count < 2 ||
            !IsKeyword(keywords[0], "VAR") || !IsKeyword(keywords[^1], "RETURN") ||
            !string.IsNullOrWhiteSpace(normalized[..keywords[0].Start]))
        {
            return Failure(DaxVarReturnDiagnosticReason.StructureRejected);
        }

        // The boundary between declarations is deliberately stricter than
        // general DAX. Failed validation is a structural bucket, not a claim
        // that the expression is invalid for Microsoft Analysis Services.
        for (var index = 1; index < keywords.Count; index++)
        {
            if (keywords[index].Start == 0 ||
                !char.IsWhiteSpace(normalized[keywords[index].Start - 1]))
            {
                return Failure(DaxVarReturnDiagnosticReason.StructureRejected);
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
                return Failure(DaxVarReturnDiagnosticReason.StructureRejected);
            }
            variables.Add(new Definition(name, value));
        }

        var returned = normalized[keywords[^1].End..].Trim();
        if (returned.Length == 0)
        {
            return Failure(DaxVarReturnDiagnosticReason.StructureRejected);
        }

        var remainingSteps = MaxResolutionSteps;
        return Resolve(returned, variables, variables.Count, 0, false, ref remainingSteps);
    }

    private static DaxVarReturnDiagnostic Resolve(string expression,
        IReadOnlyList<Definition> variables, int availableCount, int depth,
        bool isVariableValue, ref int remainingSteps)
    {
        if (depth > MaxAliasDepth || --remainingSteps < 0)
        {
            return Failure(DaxVarReturnDiagnosticReason.AnalysisLimitExceeded);
        }

        var evidence = StaticDaxTypeEvidence.Analyze(expression);
        if (evidence != StaticDaxTypeEvidenceKind.Unresolved)
        {
            return new DaxVarReturnDiagnostic(evidence,
                DaxVarReturnDiagnosticReason.NotApplicable, DaxRootShapeKind.NotApplicable);
        }

        var candidate = expression.Trim();
        var rootShape = DaxRootShapeProfiler.Analyze(candidate);
        var conditionalReason = rootShape == DaxRootShapeKind.Conditional
            ? DaxConditionalResolutionKind.UnsupportedCallShape
            : DaxConditionalResolutionKind.NotApplicable;
        var firstUnknownBranchShape = DaxRootShapeKind.NotApplicable;

        // Preserve the original conservative resolution policy. Only record
        // *why* an unresolved IF/SWITCH/COALESCE call was not classified.
        // Diagnostics stop at the first unknown/conflicting branch, mirroring
        // the pre-existing inference path and step budget.
        if (DaxConditionalScalarArguments.TryExtract(candidate, out var branches))
        {
            StaticDaxTypeEvidenceKind? common = null;
            var compatible = true;
            foreach (var branch in branches)
            {
                if (DaxConditionalScalarArguments.IsExplicitBlank(branch)) continue;
                var result = Resolve(branch, variables, availableCount, depth + 1,
                    isVariableValue, ref remainingSteps);
                if (result.Reason == DaxVarReturnDiagnosticReason.AnalysisLimitExceeded)
                    return result;
                if (result.Evidence is not (StaticDaxTypeEvidenceKind.LikelyNumeric or
                    StaticDaxTypeEvidenceKind.LikelyNonNumeric))
                {
                    compatible = false;
                    conditionalReason = DaxConditionalResolutionKind.UnresolvedResultBranch;
                    firstUnknownBranchShape = result.BlockedRootShape != DaxRootShapeKind.NotApplicable
                        ? result.BlockedRootShape
                        : DaxRootShapeProfiler.Analyze(branch);
                    break;
                }
                if (common is not null && common.Value != result.Evidence)
                {
                    compatible = false;
                    conditionalReason = DaxConditionalResolutionKind.MixedResultTypes;
                    break;
                }
                common = result.Evidence;
            }
            if (compatible && common is { } kind)
                return new DaxVarReturnDiagnostic(kind,
                    DaxVarReturnDiagnosticReason.NotApplicable,
                    DaxRootShapeKind.NotApplicable);
            if (compatible)
                conditionalReason = DaxConditionalResolutionKind.OnlyBlankResults;
        }

        // CALCULATE keeps its first argument's scalar type. This is lexical,
        // advisory evidence only. Unsupported nested values retain the old
        // unresolved reason and blocked-shape classification.
        if (DaxCalculateScalarArgument.TryExtract(candidate, out var scalarArgument))
        {
            var inner = Resolve(scalarArgument, variables, availableCount,
                depth + 1, isVariableValue, ref remainingSteps);
            if (inner.Reason == DaxVarReturnDiagnosticReason.AnalysisLimitExceeded)
                return inner;
            if (inner.Evidence != StaticDaxTypeEvidenceKind.Unresolved)
            {
                return inner;
            }
        }

        if (!IsVariableName(candidate))
        {
            return Failure(isVariableValue
                ? DaxVarReturnDiagnosticReason.UnsupportedVariableExpression
                : DaxVarReturnDiagnosticReason.UnsupportedReturnExpression,
                rootShape, conditionalReason, firstUnknownBranchShape);
        }

        // Do not follow forward references. A variable's definition may only
        // refer to preceding declarations in this conservative implementation.
        for (var i = availableCount - 1; i >= 0; i--)
        {
            if (string.Equals(variables[i].Name, candidate, StringComparison.OrdinalIgnoreCase))
            {
                return Resolve(variables[i].Expression, variables, i, depth + 1,
                    true, ref remainingSteps);
            }
        }
        return Failure(DaxVarReturnDiagnosticReason.UnknownOrForwardVariable);
    }

    private static DaxVarReturnDiagnostic Failure(DaxVarReturnDiagnosticReason reason,
        DaxRootShapeKind blockedRoot = DaxRootShapeKind.NotApplicable,
        DaxConditionalResolutionKind conditionalReason = DaxConditionalResolutionKind.NotApplicable,
        DaxRootShapeKind firstUnknownBranchShape = DaxRootShapeKind.NotApplicable) =>
        new(StaticDaxTypeEvidenceKind.Unresolved, reason, blockedRoot)
        {
            ConditionalReason = conditionalReason,
            FirstUnresolvedConditionalBranchShape = firstUnknownBranchShape
        };

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

namespace PowerBIQualityGate.Core.Inspection;

/// <summary>Whether TOM exposes a usable model-level formatting definition.</summary>
public enum MeasureFormatKind
{
    Missing,
    Static,
    Dynamic
}

/// <summary>Classification of an offline TOM measure data type, not a DAX inference.</summary>
public enum MeasureDataTypeKind
{
    Numeric,
    NonNumeric,
    Unknown,
    Unsupported
}

/// <summary>
/// Distinguishes an existing dynamic format expression from a blank expression
/// or one whose value was not available in the snapshot.
/// </summary>
public enum DynamicFormatExpressionKind
{
    NotApplicable,
    Present,
    Blank,
    Unavailable
}

/// <summary>
/// One measure's metadata-only inspection result. No DAX expression or format
/// string is retained in this projection; names are for future opt-in details.
/// </summary>
public sealed record InspectedMeasure(
    string TableName,
    string MeasureName,
    bool IsVisible,
    string RawDataType,
    MeasureDataTypeKind DataTypeKind,
    MeasureFormatKind FormatKind,
    bool HasBlankStaticFormat,
    DynamicFormatExpressionKind DynamicExpressionKind,
    bool HasBothFormats)
{
    /// <summary>Auxiliary lexical evidence. Never promoted to a confirmed TOM type.</summary>
    public StaticDaxTypeEvidenceKind StaticDaxTypeEvidence { get; init; } =
        StaticDaxTypeEvidenceKind.NotAssessed;

    /// <summary>A coarse entry shape only for unresolved static DAX evidence.</summary>
    public DaxRootShapeKind UnresolvedRootShape { get; init; } = DaxRootShapeKind.NotApplicable;

    /// <summary>Independent advisory hint for unresolved VAR entry shapes only.</summary>
    public StaticDaxTypeEvidenceKind VarReturnEvidence { get; init; } =
        StaticDaxTypeEvidenceKind.NotAssessed;

    /// <summary>Fixed-only advisory reason; no DAX or variable name is retained.</summary>
    public DaxVarReturnDiagnosticReason VarReturnReason { get; init; } =
        DaxVarReturnDiagnosticReason.NotApplicable;
    public DaxRootShapeKind VarReturnBlockedShape { get; init; } = DaxRootShapeKind.NotApplicable;
}

/// <summary>
/// Two independent, mutually exclusive partitions of VISIBLE measures: format
/// presence (Static/Dynamic/Missing) and TOM data-type classification.
/// Hidden measures remain in the inventory but not in these partitions.
/// </summary>
public sealed record MeasureInspectionSummary(
    int Total,
    int Visible,
    int Hidden,
    int StaticFormats,
    int DynamicFormats,
    int MissingFormats,
    int NumericTypes,
    int NonNumericTypes,
    int UnknownTypes,
    int UnsupportedTypes,
    int BlankStaticFormats,
    int BlankDynamicExpressions,
    int UnavailableDynamicExpressions,
    int BothFormats)
{
    public int Inspected => Total;

    // Advisory hints for visible measures whose offline TOM result type is
    // Unknown or Unsupported. These counts NEVER enter SM-002 gate coverage.
    public int StaticDaxLikelyNumeric { get; init; }
    public int StaticDaxLikelyNonNumeric { get; init; }
    public int StaticDaxUnresolved { get; init; }
    public int StaticDaxLikelyNumericMissingFormat { get; init; }
    public IReadOnlyList<DaxRootShapeCount> UnresolvedRootShapes { get; init; } = [];

    // Additive VAR / RETURN advisory breakdown. Does not change the existing
    // StaticDax* counts, UnresolvedRootShapes or gate-coverage denominators.
    public int VarReturnLikelyNumeric { get; init; }
    public int VarReturnLikelyNonNumeric { get; init; }
    public int VarReturnUnresolved { get; init; }
    public int VarReturnLikelyNumericMissingFormat { get; init; }
    public int VarReturnAnalyzed =>
        VarReturnLikelyNumeric + VarReturnLikelyNonNumeric + VarReturnUnresolved;

    /// <summary>Optional fixed-label diagnostics for unresolved VAR/RETURN only.</summary>
    public IReadOnlyList<DaxVarReturnReasonCount> VarReturnUnresolvedReasons { get; init; } = [];
    public IReadOnlyList<DaxRootShapeCount> VarReturnBlockedShapes { get; init; } = [];
}

public sealed record MeasureInspectionReport(
    IReadOnlyList<InspectedMeasure> Measures,
    MeasureInspectionSummary Summary);

/// <summary>
/// Inspects ALL measures in a TOM snapshot. Does not execute DAX, diagnose
/// formatting correctness, or change the existing SM-002 warning policy.
/// </summary>
public sealed class MeasureInspectionEngine
{
    public MeasureInspectionReport Inspect(TomModelSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var measures = snapshot.Tables
            .OrderBy(table => table.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(table => table.Name, StringComparer.Ordinal)
            .SelectMany(table => table.Measures
                .OrderBy(measure => measure.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(measure => measure.Name, StringComparer.Ordinal)
                .Select(measure => InspectMeasure(table, measure)))
            .ToArray();

        var visible = measures.Where(measure => measure.IsVisible).ToArray();
        var summary = new MeasureInspectionSummary(
            Total: measures.Length,
            Visible: visible.Length,
            Hidden: measures.Length - visible.Length,
            StaticFormats: visible.Count(measure => measure.FormatKind == MeasureFormatKind.Static),
            DynamicFormats: visible.Count(measure => measure.FormatKind == MeasureFormatKind.Dynamic),
            MissingFormats: visible.Count(measure => measure.FormatKind == MeasureFormatKind.Missing),
            NumericTypes: visible.Count(measure => measure.DataTypeKind == MeasureDataTypeKind.Numeric),
            NonNumericTypes: visible.Count(measure => measure.DataTypeKind == MeasureDataTypeKind.NonNumeric),
            UnknownTypes: visible.Count(measure => measure.DataTypeKind == MeasureDataTypeKind.Unknown),
            UnsupportedTypes: visible.Count(measure => measure.DataTypeKind == MeasureDataTypeKind.Unsupported),
            BlankStaticFormats: visible.Count(measure => measure.HasBlankStaticFormat),
            BlankDynamicExpressions: visible.Count(measure =>
                measure.DynamicExpressionKind == DynamicFormatExpressionKind.Blank),
            UnavailableDynamicExpressions: visible.Count(measure =>
                measure.DynamicExpressionKind == DynamicFormatExpressionKind.Unavailable),
            BothFormats: visible.Count(measure => measure.HasBothFormats))
        {
            StaticDaxLikelyNumeric = visible.Count(m =>
                m.StaticDaxTypeEvidence == StaticDaxTypeEvidenceKind.LikelyNumeric),
            StaticDaxLikelyNonNumeric = visible.Count(m =>
                m.StaticDaxTypeEvidence == StaticDaxTypeEvidenceKind.LikelyNonNumeric),
            StaticDaxUnresolved = visible.Count(m =>
                m.StaticDaxTypeEvidence == StaticDaxTypeEvidenceKind.Unresolved),
            StaticDaxLikelyNumericMissingFormat = visible.Count(m =>
                m.StaticDaxTypeEvidence == StaticDaxTypeEvidenceKind.LikelyNumeric &&
                m.FormatKind == MeasureFormatKind.Missing),
            VarReturnLikelyNumeric = visible.Count(m =>
                m.VarReturnEvidence == StaticDaxTypeEvidenceKind.LikelyNumeric),
            VarReturnLikelyNonNumeric = visible.Count(m =>
                m.VarReturnEvidence == StaticDaxTypeEvidenceKind.LikelyNonNumeric),
            VarReturnUnresolved = visible.Count(m =>
                m.VarReturnEvidence == StaticDaxTypeEvidenceKind.Unresolved),
            VarReturnLikelyNumericMissingFormat = visible.Count(m =>
                m.VarReturnEvidence == StaticDaxTypeEvidenceKind.LikelyNumeric &&
                m.FormatKind == MeasureFormatKind.Missing),
            VarReturnUnresolvedReasons = Enum.GetValues<DaxVarReturnDiagnosticReason>()
                .Where(reason => reason != DaxVarReturnDiagnosticReason.NotApplicable)
                .Select(reason => new DaxVarReturnReasonCount(reason,
                    visible.Count(m => m.VarReturnReason == reason),
                    visible.Count(m => m.VarReturnReason == reason &&
                        m.FormatKind == MeasureFormatKind.Missing)))
                .Where(group => group.Count > 0)
                .ToArray(),
            VarReturnBlockedShapes = Enum.GetValues<DaxRootShapeKind>()
                .Where(shape => shape != DaxRootShapeKind.NotApplicable)
                .Select(shape => new DaxRootShapeCount(shape,
                    visible.Count(m => m.VarReturnBlockedShape == shape),
                    visible.Count(m => m.VarReturnBlockedShape == shape &&
                        m.FormatKind == MeasureFormatKind.Missing)))
                .Where(group => group.Count > 0)
                .ToArray(),
            UnresolvedRootShapes = Enum.GetValues<DaxRootShapeKind>()
                .Where(shape => shape != DaxRootShapeKind.NotApplicable)
                .Select(shape => new DaxRootShapeCount(shape,
                    visible.Count(m => m.UnresolvedRootShape == shape),
                    visible.Count(m => m.UnresolvedRootShape == shape &&
                        m.FormatKind == MeasureFormatKind.Missing)))
                .Where(group => group.Count > 0)
                .ToArray()
        };

        return new MeasureInspectionReport(measures, summary);
    }

    private static InspectedMeasure InspectMeasure(TomTableSnapshot table, TomMeasureSnapshot measure)
    {
        var staticPresent = !string.IsNullOrWhiteSpace(measure.FormatString);
        var dynamicPresent = measure.HasDynamicFormatString;
        var format = dynamicPresent
            ? MeasureFormatKind.Dynamic
            : staticPresent ? MeasureFormatKind.Static : MeasureFormatKind.Missing;

        var expression = dynamicPresent
            ? measure.DynamicFormatStringExpression switch
            {
                null => DynamicFormatExpressionKind.Unavailable,
                string value when string.IsNullOrWhiteSpace(value) => DynamicFormatExpressionKind.Blank,
                _ => DynamicFormatExpressionKind.Present
            }
            : DynamicFormatExpressionKind.NotApplicable;

        var dataType = measure.DataType?.Trim() ?? string.Empty;
        var classification = ClassifyDataType(dataType);
        var evidence = classification is MeasureDataTypeKind.Unknown or
            MeasureDataTypeKind.Unsupported
            ? StaticDaxTypeEvidence.Analyze(measure.Expression)
            : StaticDaxTypeEvidenceKind.NotAssessed;
        var rootShape = evidence == StaticDaxTypeEvidenceKind.Unresolved
            ? DaxRootShapeProfiler.Analyze(measure.Expression)
            : DaxRootShapeKind.NotApplicable;
        var varResult = !table.IsHidden && !measure.IsHidden &&
            rootShape == DaxRootShapeKind.Variable
            ? DaxVarReturnEvidence.AnalyzeDetailed(measure.Expression)
            : new DaxVarReturnDiagnostic(StaticDaxTypeEvidenceKind.NotAssessed,
                DaxVarReturnDiagnosticReason.NotApplicable, DaxRootShapeKind.NotApplicable);
        return new InspectedMeasure(
            table.Name,
            measure.Name,
            !table.IsHidden && !measure.IsHidden,
            dataType,
            classification,
            format,
            !string.IsNullOrEmpty(measure.FormatString) && !staticPresent,
            expression,
            staticPresent && dynamicPresent)
        {
            StaticDaxTypeEvidence = evidence,
            UnresolvedRootShape = rootShape,
            VarReturnEvidence = varResult.Evidence,
            VarReturnReason = varResult.Reason,
            VarReturnBlockedShape = varResult.BlockedRootShape
        };
    }

    private static MeasureDataTypeKind ClassifyDataType(string dataType)
    {
        if (IsOneOf(dataType, "Int64", "Decimal", "Double"))
        {
            return MeasureDataTypeKind.Numeric;
        }

        if (IsOneOf(dataType, "String", "Boolean", "DateTime", "Binary"))
        {
            return MeasureDataTypeKind.NonNumeric;
        }

        if (string.IsNullOrWhiteSpace(dataType) || IsOneOf(dataType, "Unknown", "Variant"))
        {
            return MeasureDataTypeKind.Unknown;
        }

        return MeasureDataTypeKind.Unsupported;
    }

    private static bool IsOneOf(string dataType, params string[] candidates) =>
        candidates.Any(candidate => string.Equals(dataType, candidate, StringComparison.OrdinalIgnoreCase));
}

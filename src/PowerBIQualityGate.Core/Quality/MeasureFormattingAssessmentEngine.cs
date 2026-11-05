using PowerBIQualityGate.Core.Inspection;

namespace PowerBIQualityGate.Core.Quality;

/// <summary>
/// Evidence-based SM-002 assessment, separate from data-type inference and
/// separate from any CLI rendering or CI gate decision.
/// </summary>
public enum MeasureFormattingAssessmentKind
{
    Hidden,
    KnownNonNumeric,
    KnownNumericWithFormat,
    KnownNumericMissingFormat,
    UnknownTypeWithFormat,
    UnknownTypeMissingFormat,
    UnsupportedTypeWithFormat,
    UnsupportedTypeMissingFormat,
    DynamicFormatNeedsReview
}

/// <summary>
/// Per-measure SM-002 evidence. Does not retain a DAX expression or a format
/// string. Object names are available only to opt-in consumers of this record.
/// </summary>
public sealed record MeasureFormattingAssessment(
    string TableName,
    string MeasureName,
    MeasureDataTypeKind DataTypeKind,
    MeasureFormatKind FormatKind,
    DynamicFormatExpressionKind DynamicExpressionKind,
    MeasureFormattingAssessmentKind Kind);

/// <summary>
/// Mutually exclusive, auditable SM-002 assessment counts. A review item is
/// not a finding and never blocks the quality gate by itself.
/// </summary>
public sealed record MeasureFormattingAssessmentSummary(
    int Total,
    int Hidden,
    int KnownNonNumeric,
    int KnownNumericWithFormat,
    int KnownNumericMissingFormat,
    int UnknownTypeWithFormat,
    int UnknownTypeMissingFormat,
    int UnsupportedTypeWithFormat,
    int UnsupportedTypeMissingFormat,
    int DynamicFormatNeedsReview)
{
    public int Visible => Total - Hidden;
    public int ConfirmedWarnings => KnownNumericMissingFormat;
    public int NeedsReview => UnknownTypeMissingFormat + UnsupportedTypeMissingFormat + DynamicFormatNeedsReview;
    public int AccountedFor => Hidden + KnownNonNumeric + KnownNumericWithFormat +
        KnownNumericMissingFormat + UnknownTypeWithFormat + UnknownTypeMissingFormat +
        UnsupportedTypeWithFormat + UnsupportedTypeMissingFormat + DynamicFormatNeedsReview;
}

public sealed record MeasureFormattingAssessmentReport(
    IReadOnlyList<MeasureFormattingAssessment> Measures,
    MeasureFormattingAssessmentSummary Summary);

/// <summary>
/// Converts the metadata inventory from MeasureInspectionEngine into an
/// actionable, conservative SM-002 assessment. Does not parse or execute DAX.
/// Numeric measures without format metadata are definite warnings; ambiguous
/// types and unverified dynamic definitions are review items only.
/// </summary>
public sealed class MeasureFormattingAssessmentEngine
{
    public MeasureFormattingAssessmentReport Assess(TomModelSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return Assess(new MeasureInspectionEngine().Inspect(snapshot));
    }

    public MeasureFormattingAssessmentReport Assess(MeasureInspectionReport inspection)
    {
        ArgumentNullException.ThrowIfNull(inspection);

        var measures = inspection.Measures.Select(item => new MeasureFormattingAssessment(
            item.TableName,
            item.MeasureName,
            item.DataTypeKind,
            item.FormatKind,
            item.DynamicExpressionKind,
            Classify(item))).ToArray();

        int Count(MeasureFormattingAssessmentKind kind) => measures.Count(item => item.Kind == kind);

        var summary = new MeasureFormattingAssessmentSummary(
            measures.Length,
            Count(MeasureFormattingAssessmentKind.Hidden),
            Count(MeasureFormattingAssessmentKind.KnownNonNumeric),
            Count(MeasureFormattingAssessmentKind.KnownNumericWithFormat),
            Count(MeasureFormattingAssessmentKind.KnownNumericMissingFormat),
            Count(MeasureFormattingAssessmentKind.UnknownTypeWithFormat),
            Count(MeasureFormattingAssessmentKind.UnknownTypeMissingFormat),
            Count(MeasureFormattingAssessmentKind.UnsupportedTypeWithFormat),
            Count(MeasureFormattingAssessmentKind.UnsupportedTypeMissingFormat),
            Count(MeasureFormattingAssessmentKind.DynamicFormatNeedsReview));

        return new MeasureFormattingAssessmentReport(Array.AsReadOnly(measures), summary);
    }

    private static MeasureFormattingAssessmentKind Classify(InspectedMeasure measure)
    {
        if (!measure.IsVisible)
        {
            return MeasureFormattingAssessmentKind.Hidden;
        }

        if (measure.DataTypeKind == MeasureDataTypeKind.NonNumeric)
        {
            return MeasureFormattingAssessmentKind.KnownNonNumeric;
        }

        // A dynamic definition may exist without an inspectable expression.
        // Never mark it as a definite formatting error, nor as verified.
        if (measure.FormatKind == MeasureFormatKind.Dynamic &&
            measure.DynamicExpressionKind is DynamicFormatExpressionKind.Blank or
                DynamicFormatExpressionKind.Unavailable)
        {
            return MeasureFormattingAssessmentKind.DynamicFormatNeedsReview;
        }

        var missing = measure.FormatKind == MeasureFormatKind.Missing;
        return measure.DataTypeKind switch
        {
            MeasureDataTypeKind.Numeric => missing
                ? MeasureFormattingAssessmentKind.KnownNumericMissingFormat
                : MeasureFormattingAssessmentKind.KnownNumericWithFormat,
            MeasureDataTypeKind.Unknown => missing
                ? MeasureFormattingAssessmentKind.UnknownTypeMissingFormat
                : MeasureFormattingAssessmentKind.UnknownTypeWithFormat,
            MeasureDataTypeKind.Unsupported => missing
                ? MeasureFormattingAssessmentKind.UnsupportedTypeMissingFormat
                : MeasureFormattingAssessmentKind.UnsupportedTypeWithFormat,
            _ => throw new InvalidOperationException("Unexpected measure data-type classification.")
        };
    }
}

using PowerBIQualityGate.Core.Inspection;

namespace PowerBIQualityGate.Core.Quality;

/// <summary>
/// Legacy SM-002 coverage categories retained for the existing CI threshold.
/// Uses the same normalized evidence as MeasureFormattingAssessmentEngine;
/// coverage is still TYPE classification coverage, not formatting validity.
/// </summary>
internal static class MeasureFormattingClassifier
{
    internal enum Category
    {
        Hidden,
        UnknownType,
        NonNumeric,
        FormattedStatic,
        FormattedDynamic,
        Unformatted
    }

    internal static Category Classify(InspectedMeasure measure)
    {
        if (!measure.IsVisible)
        {
            return Category.Hidden;
        }

        return measure.DataTypeKind switch
        {
            MeasureDataTypeKind.Numeric => measure.FormatKind switch
            {
                MeasureFormatKind.Static => Category.FormattedStatic,
                MeasureFormatKind.Dynamic => Category.FormattedDynamic,
                _ => Category.Unformatted
            },
            MeasureDataTypeKind.NonNumeric => Category.NonNumeric,
            _ => Category.UnknownType
        };
    }
}

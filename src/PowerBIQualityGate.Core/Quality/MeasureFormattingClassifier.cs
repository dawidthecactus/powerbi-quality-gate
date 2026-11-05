using PowerBIQualityGate.Core.Inspection;

namespace PowerBIQualityGate.Core.Quality;

/// <summary>
/// Shared SM-002 eligibility predicate. Coverage and findings must classify
/// measures identically; no DAX expression is parsed or executed here.
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

    internal static Category Classify(TomTableSnapshot table, TomMeasureSnapshot measure)
    {
        if (table.IsHidden || measure.IsHidden)
        {
            return Category.Hidden;
        }

        if (IsNumeric(measure.DataType))
        {
            // A dynamic format is authoritative even if a static fallback is present.
            if (measure.HasDynamicFormatString)
            {
                return Category.FormattedDynamic;
            }

            return string.IsNullOrWhiteSpace(measure.FormatString)
                ? Category.Unformatted
                : Category.FormattedStatic;
        }

        // Only these known TOM types are treated as nonnumeric by this rule.
        // Unexpected/variant/unknown types remain unclassified rather than
        // being incorrectly counted as checked or as definitely nonnumeric.
        return IsKnownNonNumeric(measure.DataType)
            ? Category.NonNumeric
            : Category.UnknownType;
    }

    private static bool IsNumeric(string? value) =>
        EqualsType(value, "Int64") ||
        EqualsType(value, "Decimal") ||
        EqualsType(value, "Double");

    private static bool IsKnownNonNumeric(string? value) =>
        EqualsType(value, "String") ||
        EqualsType(value, "Boolean") ||
        EqualsType(value, "DateTime") ||
        EqualsType(value, "Binary");

    private static bool EqualsType(string? value, string expected) =>
        string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);
}

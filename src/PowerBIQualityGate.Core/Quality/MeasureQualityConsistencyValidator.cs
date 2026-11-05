using PowerBIQualityGate.Core.Inspection;
using PowerBIQualityGate.Core.Validation;

namespace PowerBIQualityGate.Core.Quality;

/// <summary>
/// Checks aggregate invariants before publishing a measure-quality verdict.
/// Messages are stable diagnostic codes: no names, expressions or paths leave
/// the validation boundary. Does not inspect DAX or change rule severities.
/// </summary>
public static class MeasureQualityConsistencyValidator
{
    public static IReadOnlyList<string> Validate(
        TomModelSnapshot snapshot, ModelQualityAuditResult audit, ModelAuditCoverage coverage)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(coverage);

        var problems = new List<string>();
        var evaluated = audit.EvaluatedRuleCodes.Contains("SM-002", StringComparer.OrdinalIgnoreCase);
        var legacy = coverage.MeasureFormatting;
        var inventory = coverage.Sm002Inspection;
        var assessment = coverage.Sm002Assessment;

        if (!evaluated)
        {
            if (legacy is not null || inventory is not null || assessment is not null)
            {
                problems.Add("SM002_UNEVALUATED_COVERAGE");
            }
            return problems.AsReadOnly();
        }

        if (legacy is null || inventory is null || assessment is null)
        {
            problems.Add("SM002_SUMMARY_MISSING");
            return problems.AsReadOnly();
        }

        if (inventory.Total != snapshot.Measures || assessment.Total != snapshot.Measures ||
            legacy.Total != snapshot.Measures || inventory.Inspected != inventory.Total)
        {
            problems.Add("SM002_TOTAL_MISMATCH");
        }

        if (!PartitionEquals(inventory.Total, inventory.Visible, inventory.Hidden) ||
            inventory.Hidden != legacy.Hidden || assessment.Hidden != legacy.Hidden)
        {
            problems.Add("SM002_VISIBILITY_MISMATCH");
        }

        if (!PartitionEquals(inventory.Visible, inventory.StaticFormats,
                inventory.DynamicFormats, inventory.MissingFormats) ||
            !PartitionEquals(inventory.Visible, inventory.NumericTypes, inventory.NonNumericTypes,
                inventory.UnknownTypes, inventory.UnsupportedTypes))
        {
            problems.Add("SM002_INSPECTION_PARTITION_INVALID");
        }

        if (inventory.BlankStaticFormats < 0 ||
            inventory.BlankStaticFormats > (long)inventory.MissingFormats + inventory.DynamicFormats ||
            inventory.BlankDynamicExpressions < 0 || inventory.UnavailableDynamicExpressions < 0 ||
            inventory.BlankDynamicExpressions + (long)inventory.UnavailableDynamicExpressions > inventory.DynamicFormats ||
            inventory.BothFormats < 0 || inventory.BothFormats > inventory.DynamicFormats)
        {
            problems.Add("SM002_METADATA_NOTES_INVALID");
        }

        if (!PartitionEquals(legacy.Total, legacy.Hidden, legacy.UnknownType,
                legacy.NonNumeric, legacy.FormattedStatic, legacy.FormattedDynamic, legacy.Unformatted) ||
            legacy.CheckedNumeric != inventory.NumericTypes ||
            legacy.NonNumeric != inventory.NonNumericTypes ||
            legacy.UnknownType != (long)inventory.UnknownTypes + inventory.UnsupportedTypes)
        {
            problems.Add("SM002_LEGACY_COVERAGE_MISMATCH");
        }

        if (!PartitionEquals(assessment.Total,
                assessment.Hidden, assessment.KnownNonNumeric, assessment.KnownNumericWithFormat,
                assessment.KnownNumericMissingFormat, assessment.UnknownTypeWithFormat,
                assessment.UnknownTypeMissingFormat, assessment.UnsupportedTypeWithFormat,
                assessment.UnsupportedTypeMissingFormat, assessment.DynamicFormatNeedsReview) ||
            assessment.KnownNonNumeric != inventory.NonNumericTypes ||
            assessment.KnownNumericMissingFormat != legacy.Unformatted)
        {
            problems.Add("SM002_ASSESSMENT_PARTITION_INVALID");
        }

        var sm002Warnings = audit.Findings.Count(finding =>
            string.Equals(finding.RuleCode, "SM-002", StringComparison.OrdinalIgnoreCase) &&
            finding.Severity == DiagnosticSeverity.Warning);
        if (assessment.ConfirmedWarnings != sm002Warnings)
        {
            problems.Add("SM002_FINDING_COUNT_MISMATCH");
        }

        return problems.AsReadOnly();
    }

    private static bool PartitionEquals(int total, params int[] parts) =>
        total >= 0 && parts.All(part => part >= 0) && parts.Sum(part => (long)part) == total;
}

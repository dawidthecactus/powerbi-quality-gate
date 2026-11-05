using PowerBIQualityGate.Core.Inspection;

namespace PowerBIQualityGate.Core.Quality;

/// <summary>Metadata objects for which a completed rule evaluated its predicate.</summary>
public sealed record RuleObjectCoverage(string RuleCode, int Checked, int Skipped, int Total);

/// <summary>
/// A mutually exclusive partition of all TOM measures for SM-002.
/// UnknownType also includes Variant, empty and unsupported types.
/// </summary>
public sealed record MeasureFormattingCoverage(
    int Total,
    int Hidden,
    int UnknownType,
    int NonNumeric,
    int FormattedStatic,
    int FormattedDynamic,
    int Unformatted)
{
    public int Visible => Total - Hidden;
    public int CheckedNumeric => FormattedStatic + FormattedDynamic + Unformatted;
    public int Formatted => FormattedStatic + FormattedDynamic;
    public int Skipped => Hidden + UnknownType + NonNumeric;
}

/// <summary>
/// Coverage supplements a quality verdict but never changes rule severity or
/// gate exit codes. Only codes present in EvaluatedRuleCodes are reported.
/// </summary>
public sealed record ModelAuditCoverage(
    IReadOnlyList<RuleObjectCoverage> Rules,
    MeasureFormattingCoverage? MeasureFormatting)
{
    /// <summary>
    /// Conservative SM-002 assessment summary. Review counts do not generate
    /// quality findings or change the existing classification coverage gate.
    /// </summary>
    public MeasureFormattingAssessmentSummary? Sm002Assessment { get; init; }
}

public static class ModelAuditCoverageAnalyzer
{
    public static ModelAuditCoverage Analyze(TomModelSnapshot snapshot, ModelQualityAuditResult result)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(result);

        bool Evaluated(string code) =>
            result.EvaluatedRuleCodes.Contains(code, StringComparer.OrdinalIgnoreCase);

        var rules = new List<RuleObjectCoverage>();
        MeasureFormattingCoverage? formatting = null;
        MeasureFormattingAssessmentSummary? assessmentSummary = null;

        var totalRelationships = snapshot.Relationships.Count;
        var activeRelationships = snapshot.Relationships.Count(relationship => relationship.IsActive);
        var inactiveRelationships = totalRelationships - activeRelationships;

        if (Evaluated("SM-001"))
        {
            rules.Add(new RuleObjectCoverage("SM-001", activeRelationships, inactiveRelationships, totalRelationships));
        }

        if (Evaluated("SM-002"))
        {
            var inspection = new MeasureInspectionEngine().Inspect(snapshot);
            assessmentSummary = new MeasureFormattingAssessmentEngine().Assess(inspection).Summary;
            var counters = new int[Enum.GetValues<MeasureFormattingClassifier.Category>().Length];
            foreach (var measure in inspection.Measures)
            {
                counters[(int)MeasureFormattingClassifier.Classify(measure)]++;
            }

            formatting = new MeasureFormattingCoverage(
                snapshot.Measures,
                counters[(int)MeasureFormattingClassifier.Category.Hidden],
                counters[(int)MeasureFormattingClassifier.Category.UnknownType],
                counters[(int)MeasureFormattingClassifier.Category.NonNumeric],
                counters[(int)MeasureFormattingClassifier.Category.FormattedStatic],
                counters[(int)MeasureFormattingClassifier.Category.FormattedDynamic],
                counters[(int)MeasureFormattingClassifier.Category.Unformatted]);

            rules.Add(new RuleObjectCoverage("SM-002", formatting.CheckedNumeric, formatting.Skipped, formatting.Total));
        }

        if (Evaluated("SM-003"))
        {
            var visibleColumns = snapshot.Tables.Sum(table => table.IsHidden
                ? 0
                : table.Columns.Count(column => !column.IsHidden));
            rules.Add(new RuleObjectCoverage("SM-003", visibleColumns, snapshot.Columns - visibleColumns, snapshot.Columns));
        }

        if (Evaluated("SM-004"))
        {
            rules.Add(new RuleObjectCoverage("SM-004", inactiveRelationships, activeRelationships, totalRelationships));
        }

        return new ModelAuditCoverage(rules, formatting) { Sm002Assessment = assessmentSummary };
    }
}

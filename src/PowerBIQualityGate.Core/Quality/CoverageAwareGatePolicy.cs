namespace PowerBIQualityGate.Core.Quality;

/// <summary>
/// Applies an optional SM-002 coverage threshold after the existing quality
/// policy. It never changes finding severities or attempts to infer DAX types.
/// </summary>
public sealed record CoverageAwareGatePolicy(bool FailOnWarning = false, int? MinimumMeasureCoverage = null)
{
    public QualityGateDecision Evaluate(ModelQualityAuditResult audit, ModelAuditCoverage coverage)
    {
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(coverage);

        if (MinimumMeasureCoverage is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(MinimumMeasureCoverage),
                "Coverage threshold must be an integer from 0 through 100.");
        }

        var original = new QualityGatePolicy(FailOnWarning).Evaluate(audit);
        var measures = coverage.MeasureFormatting;
        // The threshold is meaningful for visible numeric or unclassified
        // measures. Hidden and known nonnumeric measures are not candidates.
        var checkedCount = measures?.CheckedNumeric ?? 0;
        var candidates = checkedCount + (measures?.UnknownType ?? 0);
        var gaps = measures is not null && measures.UnknownType > 0;
        var decision = original with
        {
            MinimumMeasureCoverage = MinimumMeasureCoverage,
            CheckedMeasures = checkedCount,
            CandidateMeasures = candidates,
            HasMeasureCoverageGaps = gaps
        };

        // A failed or incomplete quality audit retains its original reason.
        // A model with no applicable measures has N/A coverage: no threshold
        // can be evaluated, and it is not mistakenly treated as 0%.
        if (original.ExitCode != 0 || measures is null)
        {
            return decision;
        }

        // Cross-multiply integers instead of rounding a percentage before
        // comparing. At the boundary, 94.99% cannot satisfy a 95% threshold.
        if (MinimumMeasureCoverage is int minimum && candidates > 0 &&
            (long)checkedCount * 100 < (long)minimum * candidates)
        {
            return decision with { Outcome = QualityGateOutcome.FailedOnCoverage, ExitCode = 1 };
        }

        return gaps
            ? decision with { Outcome = QualityGateOutcome.PassedWithCoverageGaps }
            : decision;
    }
}

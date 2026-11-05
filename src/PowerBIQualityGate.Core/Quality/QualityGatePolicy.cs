namespace PowerBIQualityGate.Core.Quality;

/// <summary>
/// Result of applying a CI exit-code policy to a completed quality audit.
/// The audit findings and their original severity are never modified.
/// </summary>
public enum QualityGateOutcome
{
    NotEvaluated,
    Passed,
    PassedWithAdvisories,
    Failed,
    FailedOnWarnings,
    PassedWithCoverageGaps,
    FailedOnCoverage
}

public sealed record QualityGateDecision(
    QualityGateOutcome Outcome,
    int ExitCode,
    bool FailOnWarning,
    int? MinimumMeasureCoverage = null,
    int CheckedMeasures = 0,
    int CandidateMeasures = 0,
    bool HasMeasureCoverageGaps = false);

/// <summary>
/// Pure policy evaluation, separate from TOM parsing and rule execution.
/// 0 = completed and accepted, 1 = quality gate rejected findings,
/// 2 = audit was not evaluated. Structural and input errors are handled by CLI.
/// </summary>
public sealed record QualityGatePolicy(bool FailOnWarning = false)
{
    public QualityGateDecision Evaluate(ModelQualityAuditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.RulesEvaluated == 0)
        {
            return new QualityGateDecision(QualityGateOutcome.NotEvaluated, 2, FailOnWarning);
        }

        if (result.ErrorCount > 0)
        {
            return new QualityGateDecision(QualityGateOutcome.Failed, 1, FailOnWarning);
        }

        if (FailOnWarning && result.WarningCount > 0)
        {
            return new QualityGateDecision(QualityGateOutcome.FailedOnWarnings, 1, FailOnWarning);
        }

        return result.Findings.Count > 0
            ? new QualityGateDecision(QualityGateOutcome.PassedWithAdvisories, 0, FailOnWarning)
            : new QualityGateDecision(QualityGateOutcome.Passed, 0, FailOnWarning);
    }
}

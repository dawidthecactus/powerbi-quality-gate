using PowerBIQualityGate.Core.Validation;

namespace PowerBIQualityGate.Core.Quality;

public enum QualityAuditVerdict
{
    NotEvaluated,
    Passed,
    Failed
}

/// <summary>
/// Immutable audit summary for a single semantic-model snapshot.
/// In particular, zero registered rules is NOT a passing audit.
/// </summary>
public sealed record ModelQualityAuditResult(
    string ModelName,
    IReadOnlyList<string> EvaluatedRuleCodes,
    IReadOnlyList<ModelQualityFinding> Findings)
{
    public int RulesEvaluated => EvaluatedRuleCodes.Count;

    public int ErrorCount => Findings.Count(finding => finding.Severity == DiagnosticSeverity.Error);

    public int WarningCount => Findings.Count(finding => finding.Severity == DiagnosticSeverity.Warning);

    public int InformationCount => Findings.Count(finding => finding.Severity == DiagnosticSeverity.Information);

    /// <summary>Warnings and information do not fail the default gate.</summary>
    public QualityAuditVerdict Verdict => RulesEvaluated == 0
        ? QualityAuditVerdict.NotEvaluated
        : ErrorCount > 0 ? QualityAuditVerdict.Failed : QualityAuditVerdict.Passed;

    public bool IsPassing => Verdict == QualityAuditVerdict.Passed;
}

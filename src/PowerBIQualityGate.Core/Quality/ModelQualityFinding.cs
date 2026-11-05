using PowerBIQualityGate.Core.Validation;

namespace PowerBIQualityGate.Core.Quality;

/// <summary>
/// One fully qualified audit finding. Stable rule metadata comes from the
/// registered rule, not from untrusted text returned by the evaluator.
/// </summary>
public sealed record ModelQualityFinding(
    string RuleCode,
    string RuleTitle,
    DiagnosticSeverity Severity,
    string Location,
    string Message,
    string WhyItMatters,
    string HowToFix);

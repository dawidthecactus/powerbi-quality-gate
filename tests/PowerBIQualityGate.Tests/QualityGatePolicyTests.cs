using PowerBIQualityGate.Core.Quality;
using PowerBIQualityGate.Core.Validation;

namespace PowerBIQualityGate.Tests;

public sealed class QualityGatePolicyTests
{
    [Fact]
    public void NoRulesCannotBeReportedAsPass()
    {
        var decision = new QualityGatePolicy().Evaluate(Audit([], withRules: false));

        Assert.Equal(QualityGateOutcome.NotEvaluated, decision.Outcome);
        Assert.Equal(2, decision.ExitCode);
    }

    [Fact]
    public void NoRulesCannotBeReportedAsPassWithStrictPolicy()
    {
        var decision = new QualityGatePolicy(FailOnWarning: true).Evaluate(Audit([], withRules: false));

        Assert.Equal(QualityGateOutcome.NotEvaluated, decision.Outcome);
        Assert.Equal(2, decision.ExitCode);
    }

    [Fact]
    public void NoFindingsPassesWithStrictPolicy()
    {
        var decision = new QualityGatePolicy(FailOnWarning: true).Evaluate(Audit([], withRules: true));

        Assert.Equal(QualityGateOutcome.Passed, decision.Outcome);
        Assert.Equal(0, decision.ExitCode);
        Assert.True(decision.FailOnWarning);
    }

    [Fact]
    public void DefaultPolicyAllowsWarnings()
    {
        var decision = new QualityGatePolicy().Evaluate(Audit([DiagnosticSeverity.Warning]));

        Assert.Equal(QualityGateOutcome.PassedWithAdvisories, decision.Outcome);
        Assert.Equal(0, decision.ExitCode);
        Assert.False(decision.FailOnWarning);
    }

    [Fact]
    public void StrictPolicyRejectsWarnings()
    {
        var decision = new QualityGatePolicy(FailOnWarning: true).Evaluate(Audit([DiagnosticSeverity.Warning]));

        Assert.Equal(QualityGateOutcome.FailedOnWarnings, decision.Outcome);
        Assert.Equal(1, decision.ExitCode);
    }

    [Fact]
    public void InformationOnlyNeverFailsStrictPolicy()
    {
        var decision = new QualityGatePolicy(FailOnWarning: true).Evaluate(Audit([DiagnosticSeverity.Information]));

        Assert.Equal(QualityGateOutcome.PassedWithAdvisories, decision.Outcome);
        Assert.Equal(0, decision.ExitCode);
    }

    [Fact]
    public void ErrorAlwaysFailsDefaultPolicy()
    {
        var decision = new QualityGatePolicy().Evaluate(Audit([DiagnosticSeverity.Error]));

        Assert.Equal(QualityGateOutcome.Failed, decision.Outcome);
        Assert.Equal(1, decision.ExitCode);
    }

    [Fact]
    public void ErrorTakesPrecedenceOverWarningPolicy()
    {
        var decision = new QualityGatePolicy(FailOnWarning: true).Evaluate(
            Audit([DiagnosticSeverity.Error, DiagnosticSeverity.Warning]));

        Assert.Equal(QualityGateOutcome.Failed, decision.Outcome);
        Assert.Equal(1, decision.ExitCode);
    }

    [Fact]
    public void StrictPolicyDoesNotChangeFindingSeverityOrCounts()
    {
        var audit = Audit([DiagnosticSeverity.Warning, DiagnosticSeverity.Information]);

        var decision = new QualityGatePolicy(FailOnWarning: true).Evaluate(audit);

        Assert.Equal(QualityGateOutcome.FailedOnWarnings, decision.Outcome);
        Assert.Equal(0, audit.ErrorCount);
        Assert.Equal(1, audit.WarningCount);
        Assert.Equal(1, audit.InformationCount);
        Assert.Equal(QualityAuditVerdict.Passed, audit.Verdict);
    }

    [Fact]
    public void NullAuditIsRejected()
    {
        Assert.Throws<ArgumentNullException>(() => new QualityGatePolicy().Evaluate(null!));
    }

    private static ModelQualityAuditResult Audit(DiagnosticSeverity[] severities, bool withRules = true)
    {
        string[] codes = withRules ? ["SM-001"] : [];
        ModelQualityFinding[] findings = severities.Select((severity, i) => new ModelQualityFinding(
            "SM-001", "Example rule", severity, $"Sales[Column{i}]",
            "Finding", "Reason", "Fix")).ToArray();
        return new ModelQualityAuditResult("Demo", codes, findings);
    }
}

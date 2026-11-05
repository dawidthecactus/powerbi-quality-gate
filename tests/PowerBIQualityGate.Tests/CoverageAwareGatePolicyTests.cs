using PowerBIQualityGate.Core.Inspection;
using PowerBIQualityGate.Core.Quality;
using PowerBIQualityGate.Core.Validation;

namespace PowerBIQualityGate.Tests;

public sealed class CoverageAwareGatePolicyTests
{
    [Fact]
    public void NielsenLikeUnknownTypesReportNonBlockingCoverageGapByDefault()
    {
        var (audit, coverage) = EvaluateModel(0, 763);

        var decision = new CoverageAwareGatePolicy().Evaluate(audit, coverage);

        Assert.Equal(QualityGateOutcome.PassedWithCoverageGaps, decision.Outcome);
        Assert.Equal(0, decision.ExitCode);
        Assert.Equal(763, decision.CandidateMeasures);
        Assert.Equal(0, decision.CheckedMeasures);
        Assert.True(decision.HasMeasureCoverageGaps);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(95, 1)]
    [InlineData(100, 1)]
    public void ZeroClassificationIsOnlyAllowedWhenRequiredThresholdIsZero(
        int threshold, int expectedExit)
    {
        var (audit, coverage) = EvaluateModel(0, 763);

        var decision = new CoverageAwareGatePolicy(MinimumMeasureCoverage: threshold)
            .Evaluate(audit, coverage);

        Assert.Equal(expectedExit, decision.ExitCode);
        Assert.Equal(expectedExit == 0
            ? QualityGateOutcome.PassedWithCoverageGaps
            : QualityGateOutcome.FailedOnCoverage, decision.Outcome);
    }

    [Theory]
    [InlineData(94, 94, 6, 0)]
    [InlineData(95, 94, 6, 1)]
    [InlineData(95, 95, 5, 0)]
    [InlineData(100, 99, 1, 1)]
    [InlineData(100, 100, 0, 0)]
    [InlineData(95, 9499, 501, 1)]
    public void ExactCoverageBoundaryNeverRoundsUp(int threshold, int numeric,
        int unknown, int expectedExit)
    {
        var (audit, coverage) = EvaluateModel(numeric, unknown);

        var decision = new CoverageAwareGatePolicy(MinimumMeasureCoverage: threshold)
            .Evaluate(audit, coverage);

        Assert.Equal(expectedExit, decision.ExitCode);
    }

    [Fact]
    public void KnownNonNumericAndHiddenMeasuresAreExcludedFromDenominator()
    {
        var (audit, coverage) = EvaluateModel(3, 1, nonNumeric: 6, hidden: 2);

        var decision = new CoverageAwareGatePolicy(MinimumMeasureCoverage: 75)
            .Evaluate(audit, coverage);

        Assert.Equal(3, decision.CheckedMeasures);
        Assert.Equal(4, decision.CandidateMeasures);
        Assert.Equal(0, decision.ExitCode);
        Assert.True(decision.HasMeasureCoverageGaps);
    }

    [Fact]
    public void MissingCandidatesAreNotInterpretedAsZeroPercent()
    {
        var (audit, coverage) = EvaluateModel(0, 0, nonNumeric: 12);

        var decision = new CoverageAwareGatePolicy(MinimumMeasureCoverage: 100)
            .Evaluate(audit, coverage);

        Assert.Equal(0, decision.CandidateMeasures);
        Assert.Equal(0, decision.ExitCode);
        Assert.False(decision.HasMeasureCoverageGaps);
    }

    [Fact]
    public void WarningFailureTakesPrecedenceOverCoverageFailure()
    {
        var (audit, coverage) = EvaluateModel(0, 5, warning: true);

        var decision = new CoverageAwareGatePolicy(FailOnWarning: true,
            MinimumMeasureCoverage: 95).Evaluate(audit, coverage);

        Assert.Equal(QualityGateOutcome.FailedOnWarnings, decision.Outcome);
        Assert.Equal(1, decision.ExitCode);
    }

    [Fact]
    public void ErrorFindingTakesPrecedenceOverCoverageFailure()
    {
        var (audit, coverage) = EvaluateModel(0, 5, error: true);

        var decision = new CoverageAwareGatePolicy(MinimumMeasureCoverage: 95)
            .Evaluate(audit, coverage);

        Assert.Equal(QualityGateOutcome.Failed, decision.Outcome);
        Assert.Equal(1, decision.ExitCode);
    }

    [Fact]
    public void ZeroRulesKeepNotEvaluatedExitCodeTwo()
    {
        var (audit, coverage) = EvaluateModel(0, 4, noRules: true);

        var decision = new CoverageAwareGatePolicy(MinimumMeasureCoverage: 95)
            .Evaluate(audit, coverage);

        Assert.Equal(QualityGateOutcome.NotEvaluated, decision.Outcome);
        Assert.Equal(2, decision.ExitCode);
    }

    [Fact]
    public void WithoutSm002EvaluationDoNotInventCoverage()
    {
        var snapshot = Model(0, 5);
        var audit = new ModelQualityAuditResult("Model", ["SM-003"], []);
        var coverage = ModelAuditCoverageAnalyzer.Analyze(snapshot, audit);

        var decision = new CoverageAwareGatePolicy(MinimumMeasureCoverage: 99)
            .Evaluate(audit, coverage);

        Assert.Equal(0, decision.ExitCode);
        Assert.Equal(0, decision.CandidateMeasures);
        Assert.False(decision.HasMeasureCoverageGaps);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void InvalidThresholdIsRejected(int minimum)
    {
        var (audit, coverage) = EvaluateModel(1, 1);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CoverageAwareGatePolicy(MinimumMeasureCoverage: minimum)
                .Evaluate(audit, coverage));
    }

    [Fact]
    public void NullAuditAndCoverageAreRejected()
    {
        var (audit, coverage) = EvaluateModel(1, 1);

        Assert.Throws<ArgumentNullException>(() =>
            new CoverageAwareGatePolicy().Evaluate(null!, coverage));
        Assert.Throws<ArgumentNullException>(() =>
            new CoverageAwareGatePolicy().Evaluate(audit, null!));
    }

    private static (ModelQualityAuditResult Audit, ModelAuditCoverage Coverage) EvaluateModel(
        int numeric, int unknown, int nonNumeric = 0, int hidden = 0,
        bool warning = false, bool error = false, bool noRules = false)
    {
        var model = Model(numeric, unknown, nonNumeric, hidden);
        var findings = new List<ModelQualityFinding>();
        if (warning)
        {
            findings.Add(new ModelQualityFinding("SM-001", "Sample", DiagnosticSeverity.Warning,
                "Fact[Key]", "Warning", "Why", "Fix"));
        }
        if (error)
        {
            findings.Add(new ModelQualityFinding("SM-999", "Sample", DiagnosticSeverity.Error,
                "Fact[Key]", "Error", "Why", "Fix"));
        }
        var audit = new ModelQualityAuditResult(model.Name,
            noRules ? [] : ["SM-001", "SM-002"], findings);
        return (audit, ModelAuditCoverageAnalyzer.Analyze(model, audit));
    }

    private static TomModelSnapshot Model(int numeric, int unknown,
        int nonNumeric = 0, int hidden = 0)
    {
        var measures = Enumerable.Range(0, numeric)
            .Select(i => new TomMeasureSnapshot($"N{i}", "1", "0", false, "Double"))
            .Concat(Enumerable.Range(0, unknown)
                .Select(i => new TomMeasureSnapshot($"U{i}", "1", null, false)))
            .Concat(Enumerable.Range(0, nonNumeric)
                .Select(i => new TomMeasureSnapshot($"S{i}", "\"x\"", null, false, "String")))
            .Concat(Enumerable.Range(0, hidden)
                .Select(i => new TomMeasureSnapshot($"H{i}", "1", null, true, "Double")))
            .ToArray();
        return new TomModelSnapshot("Model", 1600,
            [new TomTableSnapshot("Fact", false, [], measures, 0, 1)], []);
    }
}

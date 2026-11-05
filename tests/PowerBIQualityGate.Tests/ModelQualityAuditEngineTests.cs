using PowerBIQualityGate.Core.Inspection;
using PowerBIQualityGate.Core.Quality;
using PowerBIQualityGate.Core.Validation;

namespace PowerBIQualityGate.Tests;

public sealed class ModelQualityAuditEngineTests
{
    [Fact]
    public void NoRules_IsNotEvaluatedAndNeverPasses()
    {
        var result = new ModelQualityAuditEngine([]).Run(SampleModel());

        Assert.Equal(QualityAuditVerdict.NotEvaluated, result.Verdict);
        Assert.False(result.IsPassing);
        Assert.Equal(0, result.RulesEvaluated);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void RuleWithoutIssues_Passes()
    {
        var result = new ModelQualityAuditEngine([
            Rule("SM-001", DiagnosticSeverity.Warning)
        ]).Run(SampleModel());

        Assert.Equal(QualityAuditVerdict.Passed, result.Verdict);
        Assert.True(result.IsPassing);
        Assert.Equal(1, result.RulesEvaluated);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void WarningsAndInformation_DoNotFailDefaultGate()
    {
        var result = new ModelQualityAuditEngine([
            Rule("SM-002", DiagnosticSeverity.Information, Issue("Sales[Key]")),
            Rule("SM-001", DiagnosticSeverity.Warning, Issue("Sales[Amount]"))
        ]).Run(SampleModel());

        Assert.Equal(QualityAuditVerdict.Passed, result.Verdict);
        Assert.Equal(1, result.WarningCount);
        Assert.Equal(1, result.InformationCount);
        Assert.Equal(0, result.ErrorCount);
        Assert.Equal("SM-001", result.Findings[0].RuleCode);
    }

    [Fact]
    public void ErrorFinding_FailsAndPreservesGuidance()
    {
        var result = new ModelQualityAuditEngine([
            Rule("SM-003", DiagnosticSeverity.Error, Issue("Sales[Amount]"))
        ]).Run(SampleModel());

        Assert.Equal(QualityAuditVerdict.Failed, result.Verdict);
        Assert.False(result.IsPassing);
        Assert.Equal(1, result.ErrorCount);
        var finding = Assert.Single(result.Findings);
        Assert.Equal("SM-003", finding.RuleCode);
        Assert.Equal("Rule SM-003", finding.RuleTitle);
        Assert.Equal("Sales[Amount]", finding.Location);
        Assert.Equal("A finding", finding.Message);
        Assert.Equal("Why it matters", finding.WhyItMatters);
        Assert.Equal("How to fix it", finding.HowToFix);
    }

    [Fact]
    public void Findings_AreSortedBySeverityCodeAndLocation()
    {
        var result = new ModelQualityAuditEngine([
            Rule("SM-020", DiagnosticSeverity.Warning, Issue("Z"), Issue("A")),
            Rule("SM-010", DiagnosticSeverity.Error, Issue("X")),
            Rule("SM-004", DiagnosticSeverity.Warning, Issue("Y"))
        ]).Run(SampleModel());

        Assert.Equal(
            new[] { "SM-010:X", "SM-004:Y", "SM-020:A", "SM-020:Z" },
            result.Findings.Select(finding => $"{finding.RuleCode}:{finding.Location}"));
    }

    [Fact]
    public void RulesExecuteInStableCodeOrder()
    {
        var calls = new List<string>();
        var first = Rule("SM-010", DiagnosticSeverity.Warning, model =>
        {
            calls.Add("SM-010");
            return [];
        });
        var second = Rule("SM-002", DiagnosticSeverity.Warning, model =>
        {
            calls.Add("SM-002");
            return [];
        });

        var result = new ModelQualityAuditEngine([first, second]).Run(SampleModel());

        Assert.Equal(new[] { "SM-002", "SM-010" }, calls);
        Assert.Equal(new[] { "SM-002", "SM-010" }, result.EvaluatedRuleCodes);
    }

    [Fact]
    public void DuplicateCodes_AreRejected()
    {
        Assert.Throws<ArgumentException>(() => new ModelQualityAuditEngine([
            Rule("SM-001", DiagnosticSeverity.Warning),
            Rule("SM-001", DiagnosticSeverity.Error)
        ]));
    }

    [Theory]
    [InlineData("Q-001")]
    [InlineData("sm-001")]
    [InlineData("SM-1")]
    [InlineData("SM-1000")]
    [InlineData("SM-000")]
    [InlineData("SM-001\n")]
    public void InvalidRuleCodes_AreRejected(string code)
    {
        Assert.Throws<ArgumentException>(() => new ModelQualityAuditEngine([
            Rule(code, DiagnosticSeverity.Warning)
        ]));
    }

    [Fact]
    public void MissingTitle_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => new ModelQualityAuditEngine([
            new StubRule("SM-001", " ", DiagnosticSeverity.Warning, model => [])
        ]));
    }

    [Fact]
    public void InvalidSeverity_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => new ModelQualityAuditEngine([
            Rule("SM-001", (DiagnosticSeverity)123)
        ]));
    }

    [Fact]
    public void ThrowingRule_IsWrappedWithCodeAndOriginalException()
    {
        var rootCause = new InvalidOperationException("Rule failure");
        var rule = Rule("SM-099", DiagnosticSeverity.Warning, model => throw rootCause);
        var engine = new ModelQualityAuditEngine([rule]);

        var exception = Assert.Throws<ModelQualityRuleExecutionException>(() => engine.Run(SampleModel()));

        Assert.Equal("SM-099", exception.RuleCode);
        Assert.Same(rootCause, exception.InnerException);
    }

    [Fact]
    public void NullIssueCollection_IsNotTreatedAsCleanAudit()
    {
        var rule = Rule("SM-001", DiagnosticSeverity.Warning, model => null!);

        var exception = Assert.Throws<ModelQualityRuleExecutionException>(() =>
            new ModelQualityAuditEngine([rule]).Run(SampleModel()));

        Assert.IsType<InvalidOperationException>(exception.InnerException);
    }

    [Theory]
    [InlineData("", "A finding", "Why it matters", "How to fix it")]
    [InlineData("Sales[Amount]", " ", "Why it matters", "How to fix it")]
    [InlineData("Sales[Amount]", "A finding", "", "How to fix it")]
    [InlineData("Sales[Amount]", "A finding", "Why it matters", "")]
    public void IncompleteIssue_IsNotSilentlyAccepted(
        string location, string message, string why, string fix)
    {
        var rule = Rule("SM-001", DiagnosticSeverity.Warning,
            new ModelQualityIssue(location, message, why, fix));

        Assert.Throws<ModelQualityRuleExecutionException>(() =>
            new ModelQualityAuditEngine([rule]).Run(SampleModel()));
    }

    [Fact]
    public void RepeatedRuns_DoNotAccumulateFindings()
    {
        var engine = new ModelQualityAuditEngine([
            Rule("SM-001", DiagnosticSeverity.Warning, Issue("Sales[Amount]"))
        ]);
        var model = SampleModel();

        var first = engine.Run(model);
        var second = engine.Run(model);

        Assert.Single(first.Findings);
        Assert.Single(second.Findings);
        Assert.Equal("Demo", model.Name);
        Assert.Single(model.Tables);
    }

    private static TomModelSnapshot SampleModel() => new(
        "Demo", 1600,
        [new TomTableSnapshot(
            "Sales", false,
            [new TomColumnSnapshot("Amount", "Decimal", "Data", false, null)],
            [new TomMeasureSnapshot("Revenue", "SUM(Sales[Amount])", null, false)],
            0, 0)],
        []);

    private static ModelQualityIssue Issue(string location) =>
        new(location, "A finding", "Why it matters", "How to fix it");

    private static IModelQualityRule Rule(
        string code, DiagnosticSeverity severity, params ModelQualityIssue[] issues) =>
        new StubRule(code, $"Rule {code}", severity, model => issues);

    private static IModelQualityRule Rule(
        string code,
        DiagnosticSeverity severity,
        Func<TomModelSnapshot, IReadOnlyList<ModelQualityIssue>> evaluate) =>
        new StubRule(code, $"Rule {code}", severity, evaluate);

    private sealed class StubRule(
        string code,
        string title,
        DiagnosticSeverity severity,
        Func<TomModelSnapshot, IReadOnlyList<ModelQualityIssue>> evaluate) : IModelQualityRule
    {
        public string Code => code;
        public string Title => title;
        public DiagnosticSeverity Severity => severity;
        public IReadOnlyList<ModelQualityIssue> Evaluate(TomModelSnapshot model) => evaluate(model);
    }
}

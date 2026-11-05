using PowerBIQualityGate.Core.Inspection;
using PowerBIQualityGate.Core.Quality;
using PowerBIQualityGate.Core.Quality.Rules;

namespace PowerBIQualityGate.Tests;

public sealed class MeasureFormattingAssessmentTests
{
    [Fact]
    public void AssessmentSeparatesConfirmedMissingFromUnknownTypeReview()
    {
        var snapshot = Model(
            Measure("KnownMissing", "Decimal"),
            Measure("KnownStatic", "Int64", "#,##0"),
            Measure("KnownDynamic", "Double", dynamicFormat: true, expression: "\"0.0%\""),
            Measure("UnknownMissing", "Unknown"),
            Measure("UnknownStatic", "Unknown", "0.00"),
            Measure("UnsupportedMissing", "FutureType"),
            Measure("UnsupportedStatic", "FutureType", "0.00"),
            Measure("Text", "String"),
            Measure("Hidden", "Int64", hidden: true));

        var result = new MeasureFormattingAssessmentEngine().Assess(snapshot);
        var summary = result.Summary;

        Assert.Equal(9, summary.Total);
        Assert.Equal(8, summary.Visible);
        Assert.Equal(1, summary.Hidden);
        Assert.Equal(1, summary.KnownNumericMissingFormat);
        Assert.Equal(2, summary.KnownNumericWithFormat);
        Assert.Equal(1, summary.UnknownTypeMissingFormat);
        Assert.Equal(1, summary.UnknownTypeWithFormat);
        Assert.Equal(1, summary.UnsupportedTypeMissingFormat);
        Assert.Equal(1, summary.UnsupportedTypeWithFormat);
        Assert.Equal(1, summary.KnownNonNumeric);
        Assert.Equal(0, summary.DynamicFormatNeedsReview);
        Assert.Equal(1, summary.ConfirmedWarnings);
        Assert.Equal(2, summary.NeedsReview);
        Assert.Equal(summary.Total, summary.AccountedFor);
        Assert.Single(new UnformattedNumericMeasureRule().Evaluate(snapshot));
    }

    [Theory]
    [InlineData("Int64")]
    [InlineData("Decimal")]
    [InlineData("Double")]
    [InlineData(" DECIMAL ")]
    public void KnownNumericWithoutFormatIsDefiniteSm002Warning(string type)
    {
        var snapshot = Model(Measure("Missing", type));
        var assessment = Assert.Single(new MeasureFormattingAssessmentEngine().Assess(snapshot).Measures);
        var finding = Assert.Single(new UnformattedNumericMeasureRule().Evaluate(snapshot));

        Assert.Equal(MeasureFormattingAssessmentKind.KnownNumericMissingFormat, assessment.Kind);
        Assert.Equal("Data[Missing]", finding.Location);
    }

    [Theory]
    [InlineData("Unknown")]
    [InlineData("Variant")]
    [InlineData("")]
    [InlineData("  ")]
    public void UnrecognizedTypeWithoutFormatIsReviewNotWarning(string type)
    {
        var snapshot = Model(Measure("Uncertain", type));
        var assessment = Assert.Single(new MeasureFormattingAssessmentEngine().Assess(snapshot).Measures);

        Assert.Equal(MeasureFormattingAssessmentKind.UnknownTypeMissingFormat, assessment.Kind);
        Assert.Empty(new UnformattedNumericMeasureRule().Evaluate(snapshot));
    }

    [Fact]
    public void UnknownTypeWithFormatHasEvidenceWithoutClaimingNumericClassification()
    {
        var snapshot = Model(Measure("Ambiguous", "Unknown", "0.0%"));
        var assessment = new MeasureFormattingAssessmentEngine().Assess(snapshot);

        Assert.Equal(1, assessment.Summary.UnknownTypeWithFormat);
        Assert.Equal(0, assessment.Summary.ConfirmedWarnings);
        Assert.Equal(0, assessment.Summary.NeedsReview);
        Assert.Empty(new UnformattedNumericMeasureRule().Evaluate(snapshot));
    }

    [Fact]
    public void WhitespaceStaticFormatIsConfirmedMissingOnlyForNumericType()
    {
        var snapshot = Model(
            Measure("Numeric", "Decimal", "   "),
            Measure("Unknown", "Unknown", "   "));
        var summary = new MeasureFormattingAssessmentEngine().Assess(snapshot).Summary;

        Assert.Equal(1, summary.KnownNumericMissingFormat);
        Assert.Equal(1, summary.UnknownTypeMissingFormat);
        Assert.Single(new UnformattedNumericMeasureRule().Evaluate(snapshot));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankDynamicExpressionNeedsReviewRatherThanFalsePositive(string expression)
    {
        var snapshot = Model(Measure("Metric", "Double", dynamicFormat: true, expression: expression));
        var assessment = new MeasureFormattingAssessmentEngine().Assess(snapshot);

        Assert.Equal(1, assessment.Summary.DynamicFormatNeedsReview);
        Assert.Equal(0, assessment.Summary.ConfirmedWarnings);
        Assert.Equal(DynamicFormatExpressionKind.Blank, assessment.Measures[0].DynamicExpressionKind);
        Assert.Empty(new UnformattedNumericMeasureRule().Evaluate(snapshot));
    }

    [Fact]
    public void UnavailableDynamicExpressionNeedsReviewRatherThanPretendingItIsValidated()
    {
        var snapshot = Model(Measure("Metric", "Double", dynamicFormat: true));
        var assessment = new MeasureFormattingAssessmentEngine().Assess(snapshot);

        Assert.Equal(1, assessment.Summary.DynamicFormatNeedsReview);
        Assert.Equal(DynamicFormatExpressionKind.Unavailable, assessment.Measures[0].DynamicExpressionKind);
        Assert.Empty(new UnformattedNumericMeasureRule().Evaluate(snapshot));
    }

    [Fact]
    public void PresentDynamicExpressionCountsAsFormatEvidenceWithoutExecutingDax()
    {
        var snapshot = Model(Measure("Dynamic", "Double", dynamicFormat: true, expression: "SELECTEDVALUE('Format'[Pattern])"));
        var assessment = new MeasureFormattingAssessmentEngine().Assess(snapshot);

        Assert.Equal(1, assessment.Summary.KnownNumericWithFormat);
        Assert.Equal(0, assessment.Summary.NeedsReview);
        Assert.DoesNotContain("SELECTEDVALUE", assessment.Measures[0].ToString(), StringComparison.Ordinal);
        Assert.Empty(new UnformattedNumericMeasureRule().Evaluate(snapshot));
    }

    [Fact]
    public void NonNumericAndHiddenMeasuresAreNotSm002Warnings()
    {
        var snapshot = new TomModelSnapshot("Fixture", 1600,
        [
            new TomTableSnapshot("Data", false, [],
            [
                Measure("Text", "String"),
                Measure("Flag", "Boolean"),
                Measure("Hidden", "Decimal", hidden: true)
            ], 0, 1),
            new TomTableSnapshot("Secret", true, [],
                [Measure("HiddenByTable", "Decimal")], 0, 1)
        ], []);
        var result = new MeasureFormattingAssessmentEngine().Assess(snapshot);

        Assert.Equal(2, result.Summary.Hidden);
        Assert.Equal(2, result.Summary.KnownNonNumeric);
        Assert.Equal(result.Summary.Total, result.Summary.AccountedFor);
        Assert.Empty(new UnformattedNumericMeasureRule().Evaluate(snapshot));
    }

    [Fact]
    public void CoverageGateAndPreviousSm002CountsRemainCompatible()
    {
        var snapshot = Model(
            Measure("Missing", "Decimal"),
            Measure("Static", "Double", "0.00"),
            Measure("Dynamic", "Int64", dynamicFormat: true, expression: "\"#,0\""),
            Measure("Unknown", "Unknown"));
        var audit = new ModelQualityAuditEngine(ModelQualityRulePack.CreateDefault()).Run(snapshot);
        var coverage = ModelAuditCoverageAnalyzer.Analyze(snapshot, audit);
        var assessment = Assert.IsType<MeasureFormattingAssessmentSummary>(coverage.Sm002Assessment);
        var measureCoverage = Assert.IsType<MeasureFormattingCoverage>(coverage.MeasureFormatting);

        Assert.Equal(1, audit.WarningCount);
        Assert.Equal(3, measureCoverage.CheckedNumeric);
        Assert.Equal(1, measureCoverage.UnknownType);
        Assert.Equal(1, measureCoverage.Unformatted);
        Assert.Equal(1, assessment.ConfirmedWarnings);
        Assert.Equal(1, assessment.NeedsReview);
        Assert.Equal(0, new CoverageAwareGatePolicy().Evaluate(audit, coverage).ExitCode);
        Assert.Equal(1, new CoverageAwareGatePolicy(MinimumMeasureCoverage: 95)
            .Evaluate(audit, coverage).ExitCode);
    }

    [Fact]
    public void NielsenScaleRegressionHas104Formats659ReviewItemsAndZeroFalseWarnings()
    {
        var measures = Enumerable.Range(1, 763)
            .Select(i => Measure($"Synthetic{i:D4}", "Unknown", i <= 104 ? "#,##0.0" : null))
            .ToArray();
        var snapshot = Model(measures);
        var audit = new ModelQualityAuditEngine(ModelQualityRulePack.CreateDefault()).Run(snapshot);
        var coverage = ModelAuditCoverageAnalyzer.Analyze(snapshot, audit);
        var report = new MeasureFormattingAssessmentEngine().Assess(snapshot);

        Assert.Equal(763, report.Summary.Total);
        Assert.Equal(104, report.Summary.UnknownTypeWithFormat);
        Assert.Equal(659, report.Summary.UnknownTypeMissingFormat);
        Assert.Equal(659, report.Summary.NeedsReview);
        Assert.Equal(0, report.Summary.ConfirmedWarnings);
        Assert.Equal(763, report.Summary.AccountedFor);
        Assert.Equal(0, audit.WarningCount);
        Assert.Equal(763, coverage.MeasureFormatting!.UnknownType);
        Assert.Equal(report.Summary, coverage.Sm002Assessment);
        Assert.Equal(QualityGateOutcome.PassedWithCoverageGaps,
            new CoverageAwareGatePolicy().Evaluate(audit, coverage).Outcome);
        Assert.Equal(QualityGateOutcome.FailedOnCoverage,
            new CoverageAwareGatePolicy(MinimumMeasureCoverage: 95)
                .Evaluate(audit, coverage).Outcome);
    }

    [Fact]
    public void IncludedTmdlExampleIsReadOnlyAndDoesNotCreateFalseSm002Warnings()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null &&
            !File.Exists(Path.Combine(root.FullName, "PowerBIQualityGate.slnx")))
        {
            root = root.Parent;
        }

        if (root is null)
        {
            throw new DirectoryNotFoundException("Solution root was not found.");
        }

        var definition = Path.Combine(root.FullName, "examples", "valid-pbir-tmdl",
            "Example.SemanticModel", "definition");
        var source = Path.Combine(definition, "tables", "Sales.tmdl");
        var before = File.ReadAllText(source);
        var snapshot = new TomModelReader().Read(definition);
        var report = new MeasureFormattingAssessmentEngine().Assess(snapshot);

        Assert.Equal(snapshot.Measures, report.Summary.Total);
        Assert.Equal(report.Summary.Total, report.Summary.AccountedFor);
        Assert.Equal(2, report.Summary.UnknownTypeMissingFormat);
        Assert.Equal(0, report.Summary.ConfirmedWarnings);
        Assert.Empty(new UnformattedNumericMeasureRule().Evaluate(snapshot));
        Assert.Equal(before, File.ReadAllText(source));
    }

    [Fact]
    public void SummaryHasNoObjectNamesOrDaxExpressions()
    {
        var snapshot = Model(new TomMeasureSnapshot(
            "PrivateName", "SUM('Private'[Sensitive])", "0.00", false, "Unknown"));
        var summary = new MeasureFormattingAssessmentEngine().Assess(snapshot).Summary;
        var representation = summary.ToString()!;

        Assert.DoesNotContain("PrivateName", representation, StringComparison.Ordinal);
        Assert.DoesNotContain("SUM", representation, StringComparison.Ordinal);
        Assert.Equal(1, summary.UnknownTypeWithFormat);
    }

    [Fact]
    public void MultipleRunsAreDeterministicAndDoNotMutateTheSnapshot()
    {
        var original = Measure("Original", "Decimal", "0.00");
        var snapshot = Model(original);
        var engine = new MeasureFormattingAssessmentEngine();

        var first = engine.Assess(snapshot);
        var second = engine.Assess(snapshot);

        Assert.Equal(first.Summary, second.Summary);
        Assert.Equal(first.Measures.ToArray(), second.Measures.ToArray());
        Assert.Same(original, snapshot.Tables[0].Measures[0]);
        Assert.Equal("0.00", original.FormatString);
    }

    [Fact]
    public void NoSm002RuleEvaluationMeansNoClaimedSm002Assessment()
    {
        var snapshot = Model(Measure("Ignored", "Unknown"));
        var coverage = ModelAuditCoverageAnalyzer.Analyze(snapshot,
            new ModelQualityAuditResult("Fixture", ["SM-003"], []));

        Assert.Null(coverage.MeasureFormatting);
        Assert.Null(coverage.Sm002Assessment);
    }

    [Fact]
    public void EmptyModelHasEmptyAssessmentAndNoWarnings()
    {
        var snapshot = new TomModelSnapshot("Empty", 1600, [], []);
        var report = new MeasureFormattingAssessmentEngine().Assess(snapshot);

        Assert.Empty(report.Measures);
        Assert.Equal(0, report.Summary.Total);
        Assert.Equal(0, report.Summary.NeedsReview);
        Assert.Empty(new UnformattedNumericMeasureRule().Evaluate(snapshot));
    }

    [Fact]
    public void NullInputsAreRejected()
    {
        var engine = new MeasureFormattingAssessmentEngine();

        Assert.Throws<ArgumentNullException>(() => engine.Assess((TomModelSnapshot)null!));
        Assert.Throws<ArgumentNullException>(() => engine.Assess((MeasureInspectionReport)null!));
    }

    private static TomModelSnapshot Model(params TomMeasureSnapshot[] measures) => new("Fixture", 1600,
        [new TomTableSnapshot("Data", false, [], measures, 0, 1)], []);

    private static TomMeasureSnapshot Measure(string name, string type, string? format = null,
        bool hidden = false, bool dynamicFormat = false, string? expression = null) =>
        new(name, "1", format, hidden, type, dynamicFormat, expression);
}

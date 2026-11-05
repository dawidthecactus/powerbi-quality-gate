using PowerBIQualityGate.Cli.Commands;
using PowerBIQualityGate.Cli.Rendering;
using PowerBIQualityGate.Core.Inspection;
using PowerBIQualityGate.Core.Quality;

using Spectre.Console.Testing;

namespace PowerBIQualityGate.Tests;

public sealed class MeasureQualityCoverageIntegrationTests
{
    [Fact]
    public void NielsenScaleSummaryShowsAllMetadataWithoutInventingWarnings()
    {
        var model = NielsenScale();
        var (audit, coverage, decision) = Run(model);
        using var console = new TestConsole();
        console.Profile.Width = 160;
        new CompactAuditRenderer(console).Render("PublicExample.pbip", model, audit, decision,
            AuditOutputMode.Summary, coverage);

        var text = console.Output;
        Assert.Equal(0, audit.WarningCount);
        Assert.Equal(0, decision.ExitCode);
        Assert.Equal(763, coverage.Sm002Inspection!.Total);
        Assert.Equal(659, coverage.Sm002Assessment!.NeedsReview);
        Assert.Contains("MEASURE QUALITY", text);
        Assert.Contains("763/763 inspected (763 visible)", text);
        Assert.Contains("104 static", text);
        Assert.Contains("0 dynamic", text);
        Assert.Contains("659 missing", text);
        Assert.Contains("SM-002: 0 warnings", text);
        Assert.Contains("659 review", text);
        Assert.Contains("SM-002 coverage: 0.0%", text);
        Assert.Contains("numeric applicability unverified", text);
        Assert.DoesNotContain("PrivateMetric_", text);
        Assert.DoesNotContain("CONFIDENTIAL_EXPRESSION", text);
        Assert.DoesNotContain("SM-002 REVIEW BREAKDOWN", text);
        Assert.DoesNotContain("Unknown type + missing format", text);
        Assert.True(text.TrimEnd().Split('\n').Length <= 20);
    }

    [Fact]
    public void NielsenScaleDetailsReportsBreakdownOnceWithoutListingReviewObjects()
    {
        var model = NielsenScale();
        var (audit, coverage, decision) = Run(model);
        using var console = new TestConsole();
        console.Profile.Width = 160;
        new CompactAuditRenderer(console).Render("PublicExample.pbip", model, audit, decision,
            AuditOutputMode.Details, coverage);
        var text = console.Output;

        Assert.Contains("SM-002 REVIEW BREAKDOWN", text);
        Assert.Contains("Confirmed numeric missing format: 0 warnings", text);
        Assert.Contains("Unknown type + missing format: 659 review", text);
        Assert.Contains("Format present, unresolved type: 104", text);
        Assert.Contains("Review items are not findings", text);
        Assert.Contains("FINDINGS BY RULE", text);
        Assert.DoesNotContain("PrivateMetric_", text);
        Assert.DoesNotContain("CONFIDENTIAL_EXPRESSION", text);
        Assert.True(text.TrimEnd().Split('\n').Length < 40);
    }

    [Fact]
    public void QuietModeDoesNotExposeNewAggregateInformationOrChangeContract()
    {
        var model = NielsenScale();
        var (audit, coverage, decision) = Run(model);
        using var console = new TestConsole();
        new CompactAuditRenderer(console).Render("private.pbip", model, audit, decision,
            AuditOutputMode.Quiet, coverage);

        var line = console.Output;
        Assert.Single(line.TrimEnd().Split('\n'));
        Assert.Contains("PBIQ | AUDIT: PASS WITH COVERAGE GAPS", line);
        Assert.Contains("exit code: 0", line);
        Assert.DoesNotContain("MEASURE QUALITY", line);
        Assert.DoesNotContain("private.pbip", line);
        Assert.DoesNotContain("763", line);
        Assert.True(line.IndexOf('\u001b') < 0);
    }

    [Fact]
    public void NoColorModeShowsTheSameAggregatesWithoutEscapeSequences()
    {
        var model = NielsenScale();
        var (audit, coverage, decision) = Run(model);
        using var writer = new StringWriter();
        var console = ConsoleFactory.Create(writer, noColor: true);
        new CompactAuditRenderer(console).Render("Example.pbip", model, audit, decision,
            AuditOutputMode.Summary, coverage);

        var text = writer.ToString();
        Assert.Contains("104 static · 0 dynamic · 659 missing", text);
        Assert.Contains("SM-002: 0 warnings · 659 review", text);
        Assert.True(text.IndexOf('\u001b') < 0);
    }

    [Fact]
    public void MixedEvidenceSeparatesNumericWarningsReviewAndExcludedTypes()
    {
        var model = Snapshot(
            new TomMeasureSnapshot("NumericNoFormat", "1", null, false, "Double"),
            new TomMeasureSnapshot("NumericFormat", "1", "0.0", false, "Int64"),
            new TomMeasureSnapshot("UnknownNoFormat", "1", null, false, "Unknown"),
            new TomMeasureSnapshot("UnsupportedNoFormat", "1", null, false, "FutureType"),
            new TomMeasureSnapshot("DynamicBlank", "1", null, false, "Decimal", true, " "),
            new TomMeasureSnapshot("Text", "\"hello\"", null, false, "String"),
            new TomMeasureSnapshot("Hidden", "1", null, true, "Decimal"));
        var (audit, coverage, decision) = Run(model);
        using var console = new TestConsole();
        console.Profile.Width = 160;
        new CompactAuditRenderer(console).Render("Example.pbip", model, audit, decision,
            AuditOutputMode.Details, coverage);

        var text = console.Output;
        Assert.Equal(1, audit.WarningCount);
        Assert.Equal(1, coverage.Sm002Assessment!.ConfirmedWarnings);
        Assert.Equal(3, coverage.Sm002Assessment.NeedsReview);
        Assert.Equal(7, coverage.Sm002Assessment.AccountedFor);
        Assert.Contains("7/7 inspected (6 visible)", text);
        Assert.Contains("1 static", text);
        Assert.Contains("1 dynamic", text);
        Assert.Contains("4 missing", text);
        Assert.Contains("SM-002: 1 warnings · 3 review", text);
        Assert.Contains("Confirmed numeric missing format: 1 warnings", text);
        Assert.Contains("Unknown type + missing format: 1 review", text);
        Assert.Contains("Unsupported type + missing format: 1 review", text);
        Assert.Contains("Dynamic definition needs review: 1 review", text);
        Assert.Contains("Excluded: 1 known nonnumeric · 1 hidden", text);
        Assert.Contains("Fact[NumericNoFormat]", text);
        Assert.DoesNotContain("Fact[UnknownNoFormat]", text);
    }

    [Fact]
    public void StrictCoveragePolicyStillFailsWithoutChangingReviewCount()
    {
        var model = NielsenScale();
        var (audit, coverage, decision) = Run(model, minimum: 95);
        using var console = new TestConsole();
        console.Profile.Width = 160;
        new CompactAuditRenderer(console).Render("Example.pbip", model, audit, decision,
            AuditOutputMode.Summary, coverage);

        Assert.Equal(QualityGateOutcome.FailedOnCoverage, decision.Outcome);
        Assert.Equal(1, decision.ExitCode);
        Assert.Equal(0, audit.WarningCount);
        Assert.Contains("AUDIT: FAIL (COVERAGE POLICY)", console.Output);
        Assert.Contains("659 review", console.Output);
        Assert.Contains("Coverage policy: minimum 95%", console.Output);
    }

    [Fact]
    public void HiddenOnlyModelShowsInspectionButNoEligibleMeasures()
    {
        var model = Snapshot(new TomMeasureSnapshot("PrivateHidden", "1", null, true, "Decimal"));
        var (audit, coverage, decision) = Run(model, minimum: 95);
        using var console = new TestConsole();
        console.Profile.Width = 160;
        new CompactAuditRenderer(console).Render("Example.pbip", model, audit, decision,
            AuditOutputMode.Summary, coverage);

        Assert.Equal(0, decision.ExitCode);
        Assert.Equal(1, coverage.Sm002Inspection!.Total);
        Assert.Equal(0, coverage.Sm002Inspection.Visible);
        Assert.Equal(0, coverage.Sm002Assessment!.NeedsReview);
        Assert.Contains("1/1 inspected (0 visible)", console.Output);
        Assert.Contains("0 static · 0 dynamic · 0 missing", console.Output);
        Assert.Contains("SM-002 coverage: N/A", console.Output);
        Assert.DoesNotContain("PrivateHidden", console.Output);
    }

    [Fact]
    public void NoSm002RuleDoesNotInventCoverageOrMeasureQuality()
    {
        var model = Snapshot(new TomMeasureSnapshot("AnyMetric", "1", null, false, "Unknown"));
        var audit = new ModelQualityAuditResult("Demo", ["SM-003"], []);
        var coverage = ModelAuditCoverageAnalyzer.Analyze(model, audit);
        var decision = new CoverageAwareGatePolicy().Evaluate(audit, coverage);
        using var console = new TestConsole();
        console.Profile.Width = 160;
        new CompactAuditRenderer(console).Render("Example.pbip", model, audit, decision,
            AuditOutputMode.Details, coverage);

        Assert.Null(coverage.Sm002Assessment);
        Assert.Null(coverage.Sm002Inspection);
        Assert.DoesNotContain("MEASURE QUALITY", console.Output);
        Assert.DoesNotContain("SM-002 REVIEW BREAKDOWN", console.Output);
    }

    [Fact]
    public void ExistingRendererCallsWithoutCoverageKeepWorking()
    {
        var model = Snapshot(new TomMeasureSnapshot("AnyMetric", "1", null, false, "Unknown"));
        var (audit, _, decision) = Run(model);
        using var console = new TestConsole();
        console.Profile.Width = 160;
        new CompactAuditRenderer(console).Render("Example.pbip", model, audit, decision);

        Assert.Contains("SM-002 coverage: 0.0%", console.Output);
        Assert.DoesNotContain("MEASURE QUALITY", console.Output);
    }

    [Fact]
    public void RealExampleCommandSummaryIncludesMeasureCoverageFromCore()
    {
        using var output = new TestConsole();
        using var errors = new TestConsole();
        output.Profile.Width = 160;
        var exitCode = AuditCommand.Execute(ExamplePath(), output, errors);

        Assert.Equal(0, exitCode);
        Assert.Contains("MEASURE QUALITY", output.Output);
        Assert.Contains("2/2 inspected (2 visible)", output.Output);
        Assert.Contains("0 static · 0 dynamic · 2 missing", output.Output);
        Assert.Contains("SM-002: 0 warnings · 2 review", output.Output);
        Assert.Contains("SM-002 coverage: 0.0%", output.Output);
        Assert.Equal(string.Empty, errors.Output);
    }

    [Fact]
    public void RealExampleDetailsAndQuietRespectModeSwitch()
    {
        using var output = new TestConsole();
        using var errors = new TestConsole();
        output.Profile.Width = 160;
        var detailsCode = AuditCommand.Execute(ExamplePath(), output, errors, mode: AuditOutputMode.Details);
        Assert.Equal(0, detailsCode);
        Assert.Contains("SM-002 REVIEW BREAKDOWN", output.Output);
        Assert.Contains("Unknown type + missing format: 2 review", output.Output);

        using var quietOutput = new TestConsole();
        using var quietErrors = new TestConsole();
        var quietCode = AuditCommand.Execute(ExamplePath(), quietOutput, quietErrors, mode: AuditOutputMode.Quiet);
        Assert.Equal(0, quietCode);
        Assert.Single(quietOutput.Output.TrimEnd().Split('\n'));
        Assert.DoesNotContain("MEASURE QUALITY", quietOutput.Output);
        Assert.Equal(string.Empty, quietErrors.Output);
    }

    [Fact]
    public void StrictRealExampleStillWritesToErrorStream()
    {
        using var output = new TestConsole();
        using var errors = new TestConsole();
        errors.Profile.Width = 160;
        var code = AuditCommand.Execute(ExamplePath(), output, errors, minimumMeasureCoverage: 95);

        Assert.Equal(1, code);
        Assert.Equal(string.Empty, output.Output);
        Assert.Contains("MEASURE QUALITY", errors.Output);
        Assert.Contains("AUDIT: FAIL (COVERAGE POLICY)", errors.Output);
    }

    private static TomModelSnapshot NielsenScale()
    {
        var measures = Enumerable.Range(0, 104)
            .Select(i => new TomMeasureSnapshot($"PrivateMetric_{i}", "CONFIDENTIAL_EXPRESSION", "0.00", false))
            .Concat(Enumerable.Range(104, 659)
                .Select(i => new TomMeasureSnapshot($"PrivateMetric_{i}", "CONFIDENTIAL_EXPRESSION", null, false)))
            .ToArray();
        return Snapshot(measures);
    }

    private static TomModelSnapshot Snapshot(params TomMeasureSnapshot[] measures) => new("Demo", 1600,
        [new TomTableSnapshot("Fact", false, [], measures, 0, 1)], []);

    private static (ModelQualityAuditResult Audit, ModelAuditCoverage Coverage, QualityGateDecision Decision)
        Run(TomModelSnapshot snapshot, int? minimum = null)
    {
        var audit = new ModelQualityAuditEngine(ModelQualityRulePack.CreateDefault()).Run(snapshot);
        var coverage = ModelAuditCoverageAnalyzer.Analyze(snapshot, audit);
        var decision = new CoverageAwareGatePolicy(MinimumMeasureCoverage: minimum).Evaluate(audit, coverage);
        return (audit, coverage, decision);
    }

    private static string ExamplePath()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PowerBIQualityGate.slnx")))
            {
                return Path.Combine(directory.FullName, "examples", "valid-pbir-tmdl");
            }
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Solution root not found.");
    }
}

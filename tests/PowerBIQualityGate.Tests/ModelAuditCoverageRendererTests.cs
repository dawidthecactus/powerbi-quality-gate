using PowerBIQualityGate.Cli.Rendering;
using PowerBIQualityGate.Core.Inspection;
using PowerBIQualityGate.Core.Quality;
using PowerBIQualityGate.Core.Validation;

using Spectre.Console.Testing;

namespace PowerBIQualityGate.Tests;

public sealed class ModelAuditCoverageRendererTests
{
    [Fact]
    public void StyledRendererReportsCoverageWithoutChangingExistingSections()
    {
        using var console = new TestConsole();
        console.Profile.Width = 120;
        var model = Sample();
        var audit = Audit();

        new ConsoleAuditRenderer(console).Render("Example.pbip", model, audit,
            new QualityGatePolicy().Evaluate(audit));

        Assert.Contains("OBJECT COVERAGE", console.Output);
        Assert.Contains("SM-001", console.Output);
        Assert.Contains("SM-002", console.Output);
        Assert.Contains("SM-003", console.Output);
        Assert.Contains("SM-004", console.Output);
        Assert.Contains("1/3 visible measures checked for numeric formatting", console.Output);
        Assert.Contains("unknown/variant/other type 1", console.Output);
        Assert.Contains("AUDIT: PASS WITH ADVISORIES", console.Output);
        Assert.Contains("DIAGNOSTIC REPORT", console.Output);
        Assert.Contains("FINAL RESULT", console.Output);
    }

    [Fact]
    public void PlainRendererKeepsScopeCountersAndNoAnsiSequences()
    {
        using var writer = new StringWriter();
        var console = ConsoleFactory.Create(writer, noColor: true);
        var model = Sample();
        var audit = Audit();

        new ConsoleAuditRenderer(console).Render("Example.pbip", model, audit,
            new QualityGatePolicy().Evaluate(audit));

        var output = writer.ToString();
        Assert.Contains("OBJECT COVERAGE", output);
        Assert.Contains("1/3 visible measures checked for numeric formatting", output);
        Assert.Contains("formatted 0", output);
        Assert.Contains("unformatted 1", output);
        Assert.Contains("nonnumeric 1", output);
        Assert.Contains("unknown/variant/other type 1", output);
        Assert.Contains("3 total", output);
        Assert.True(output.IndexOf('\u001b') < 0);
    }

    [Fact]
    public void StrictPolicyPreservesExitOneAndRendersCoverage()
    {
        using var writer = new StringWriter();
        var console = ConsoleFactory.Create(writer, noColor: true);
        var audit = Audit();
        var decision = new QualityGatePolicy(FailOnWarning: true).Evaluate(audit);
        new ConsoleAuditRenderer(console).Render("Example.pbip", Sample(), audit, decision);

        Assert.Equal(1, decision.ExitCode);
        Assert.Contains("OBJECT COVERAGE", writer.ToString());
        Assert.Contains("AUDIT: FAIL (WARNING POLICY)", writer.ToString());
    }

    [Fact]
    public void FailureWithoutSnapshotDoesNotInventObjectCoverage()
    {
        using var writer = new StringWriter();
        var console = ConsoleFactory.Create(writer, noColor: true);
        new ConsoleAuditRenderer(console).RenderFailure(
            "Missing.pbip", "PROJECT DISCOVERY FAILED", "Missing", "Why", "Fix", 2);

        Assert.DoesNotContain("OBJECT COVERAGE", writer.ToString());
        Assert.Contains("AUDIT: NOT RUN", writer.ToString());
    }

    private static TomModelSnapshot Sample() => new("Model", 1600,
    [
        new TomTableSnapshot("Fact", false,
            [new TomColumnSnapshot("ProductId", "Int64", "Data", false, null)],
            [
                new TomMeasureSnapshot("Amount", "1", null, false, "Double"),
                new TomMeasureSnapshot("Unknown", "1", null, false),
                new TomMeasureSnapshot("Text", "\"hello\"", null, false, "String")
            ], 0, 1)
    ],
    [new TomRelationshipSnapshot("r1", "Fact[ProductId]", "Product[ProductId]", true, "BothDirections")]);

    private static ModelQualityAuditResult Audit() => new("Model",
        ["SM-001", "SM-002", "SM-003", "SM-004"],
        [new ModelQualityFinding("SM-001", "Review bidirectional relationships", DiagnosticSeverity.Warning,
            "Fact[ProductId]", "Bidirectional", "Can be ambiguous", "Consider one direction")]);
}

using PowerBIQualityGate.Cli.Rendering;
using PowerBIQualityGate.Core.Inspection;
using PowerBIQualityGate.Core.Quality;

using Spectre.Console.Testing;

namespace PowerBIQualityGate.Tests;

public sealed class CoverageAwareGateRendererTests
{
    [Theory]
    [InlineData(null, "PASS WITH COVERAGE GAPS", 0)]
    [InlineData(0, "PASS WITH COVERAGE GAPS", 0)]
    [InlineData(95, "FAIL (COVERAGE POLICY)", 1)]
    public void StyledRendererShowsExactCoverageDecision(int? minimum, string status, int exit)
    {
        using var console = new TestConsole();
        console.Profile.Width = 120;
        var (model, result, decision) = Sample(minimum);

        new ConsoleAuditRenderer(console).Render("Demo.pbip", model, result, decision);

        Assert.Contains("AUDIT: " + status, console.Output);
        Assert.Contains($"exit code: {exit}", console.Output);
        Assert.Contains("SM-002 coverage gate:", console.Output);
        Assert.Contains("Coverage gap:", console.Output);
    }

    [Theory]
    [InlineData(null, "PASS WITH COVERAGE GAPS", 0)]
    [InlineData(95, "FAIL (COVERAGE POLICY)", 1)]
    public void PlainRendererShowsStatusAndHasNoEsc(int? minimum, string status, int exit)
    {
        using var writer = new StringWriter();
        var console = ConsoleFactory.Create(writer, noColor: true);
        var (model, result, decision) = Sample(minimum);

        new ConsoleAuditRenderer(console).Render("Demo.pbip", model, result, decision);

        var text = writer.ToString();
        Assert.Contains("AUDIT: " + status, text);
        Assert.Contains($"exit code: {exit}", text);
        Assert.Contains("0.0% (0/2 candidates)", text);
        Assert.True(text.IndexOf('\u001b') < 0, "--no-color must not emit ANSI ESC.");
    }

    private static (TomModelSnapshot Model, ModelQualityAuditResult Result, QualityGateDecision Decision)
        Sample(int? minimum)
    {
        var model = new TomModelSnapshot("Demo", 1600,
            [new TomTableSnapshot("Sales", false, [],
            [
                new TomMeasureSnapshot("A", "1", null, false),
                new TomMeasureSnapshot("B", "2", null, false)
            ], 0, 1)], []);
        var audit = new ModelQualityAuditEngine(ModelQualityRulePack.CreateDefault()).Run(model);
        var coverage = ModelAuditCoverageAnalyzer.Analyze(model, audit);
        var decision = new CoverageAwareGatePolicy(MinimumMeasureCoverage: minimum)
            .Evaluate(audit, coverage);
        return (model, audit, decision);
    }
}

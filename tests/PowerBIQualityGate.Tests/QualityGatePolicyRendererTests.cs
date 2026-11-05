using PowerBIQualityGate.Cli.Rendering;
using PowerBIQualityGate.Core.Inspection;
using PowerBIQualityGate.Core.Quality;
using PowerBIQualityGate.Core.Validation;

using Spectre.Console.Testing;

namespace PowerBIQualityGate.Tests;

public sealed class QualityGatePolicyRendererTests
{
    [Fact]
    public void StyledStrictPolicyDisplaysFailNotPass()
    {
        using var console = new TestConsole();
        console.Profile.Width = 120;
        var audit = WarningAudit();
        var decision = new QualityGatePolicy(FailOnWarning: true).Evaluate(audit);

        new ConsoleAuditRenderer(console).Render("Warning.pbip", Model(), audit, decision);

        Assert.Contains("AUDIT: FAIL (WARNING POLICY)", console.Output);
        Assert.Contains("1 warning", console.Output);
        Assert.Contains("Active policy", console.Output);
        Assert.DoesNotContain("AUDIT: PASS WITH ADVISORIES", console.Output);
    }

    [Fact]
    public void PlainTextStrictPolicyDisplaysFailWithoutEscapeCodes()
    {
        using var writer = new StringWriter();
        var console = ConsoleFactory.Create(writer, noColor: true);
        var audit = WarningAudit();
        var decision = new QualityGatePolicy(FailOnWarning: true).Evaluate(audit);

        new ConsoleAuditRenderer(console).Render("Warning.pbip", Model(), audit, decision);

        var text = writer.ToString();
        Assert.Contains("AUDIT: FAIL (WARNING POLICY)", text);
        Assert.Contains("exit code: 1", text);
        Assert.Contains("SM-001", text);
        Assert.Contains("Active policy", text);
        Assert.True(text.IndexOf('\u001b') < 0, "Plain audit must contain no ESC control character.");
    }

    [Fact]
    public void DefaultPolicyPreservesAdvisoryPassInPlainText()
    {
        using var writer = new StringWriter();
        var console = ConsoleFactory.Create(writer, noColor: true);
        var audit = WarningAudit();
        var decision = new QualityGatePolicy().Evaluate(audit);

        new ConsoleAuditRenderer(console).Render("Warning.pbip", Model(), audit, decision);

        var text = writer.ToString();
        Assert.Contains("AUDIT: PASS WITH ADVISORIES", text);
        Assert.Contains("Default policy", text);
        Assert.Contains("exit code: 0", text);
    }

    [Fact]
    public void StrictPolicyDoesNotFailInformationalFinding()
    {
        using var console = new TestConsole();
        console.Profile.Width = 120;
        var audit = new ModelQualityAuditResult("Model", ["SM-003"],
        [
            new ModelQualityFinding("SM-003", "Review keys", DiagnosticSeverity.Information,
                "Sales[ProductId]", "Visible key", "Why", "Fix")
        ]);
        var decision = new QualityGatePolicy(FailOnWarning: true).Evaluate(audit);

        new ConsoleAuditRenderer(console).Render("Info.pbip", Model(), audit, decision);

        Assert.Contains("AUDIT: PASS WITH ADVISORIES", console.Output);
        Assert.Contains("Active policy", console.Output);
    }

    private static TomModelSnapshot Model() => new("Model", 1600, [], []);

    private static ModelQualityAuditResult WarningAudit() => new("Model", ["SM-001"],
    [
        new ModelQualityFinding("SM-001", "Review bidirectional relationships",
            DiagnosticSeverity.Warning, "Sales[ProductId] -> Product[ProductId]",
            "Bidirectional relationship", "Review propagation", "Use single direction if possible")
    ]);
}

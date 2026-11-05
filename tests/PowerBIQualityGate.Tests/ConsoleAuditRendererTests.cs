using PowerBIQualityGate.Cli.Rendering;
using PowerBIQualityGate.Core.Inspection;
using PowerBIQualityGate.Core.Quality;
using PowerBIQualityGate.Core.Validation;

using Spectre.Console.Testing;

namespace PowerBIQualityGate.Tests;

public sealed class ConsoleAuditRendererTests
{
    [Fact]
    public void ShowsSharedBrandSectionsAndRemediation()
    {
        using var console = new TestConsole();
        console.Profile.Width = 120;

        new ConsoleAuditRenderer(console).Render("Example.pbip", Sample(), Advisories(), 0);

        Assert.Contains("PBIQ", console.Output);
        Assert.Contains("POWER BI QUALITY GATE", console.Output);
        Assert.Contains("SEMANTIC MODEL AUDIT", console.Output);
        Assert.Contains("PROJECT PROFILE", console.Output);
        Assert.Contains("RULE COVERAGE", console.Output);
        Assert.Contains("DIAGNOSTIC REPORT", console.Output);
        Assert.Contains("FINAL RESULT", console.Output);
        Assert.Contains("SM-003", console.Output);
        Assert.Contains("WHY IT MATTERS", console.Output);
        Assert.Contains("HOW TO FIX", console.Output);
        Assert.Contains("Sales[ProductId]", console.Output);
    }

    [Fact]
    public void AdvisoriesShowPassWithAdvisoriesAndDoNotClaimCleanModel()
    {
        using var console = new TestConsole();
        console.Profile.Width = 120;

        new ConsoleAuditRenderer(console).Render("Example.pbip", Sample(), Advisories(), 0);

        Assert.Contains("AUDIT: PASS WITH ADVISORIES", console.Output);
        Assert.Contains("0 errors", console.Output);
        Assert.Contains("1 warning", console.Output);
        Assert.Contains("1 information", console.Output);
        Assert.Contains("exit code: 0", console.Output);
        Assert.DoesNotContain("No findings reported", console.Output);
        Assert.Contains("not a full model", console.Output);
    }

    [Fact]
    public void ZeroFindingsFromEvaluatedRulesIsPassButNotFullValidation()
    {
        using var console = new TestConsole();
        console.Profile.Width = 120;
        var result = new ModelQualityAuditResult("Demo", ["SM-001", "SM-002"], []);

        new ConsoleAuditRenderer(console).Render("Example.pbip", Sample(), result, 0);

        Assert.Contains("No findings reported", console.Output);
        Assert.Contains("AUDIT: PASS", console.Output);
        Assert.Contains("Other modeling problems may exist", console.Output);
    }

    [Fact]
    public void ErrorFindingsShowFailAndNonzeroExitCode()
    {
        using var console = new TestConsole();
        console.Profile.Width = 120;
        var result = new ModelQualityAuditResult("Demo", ["SM-099"],
        [
            Finding("SM-099", DiagnosticSeverity.Error, "Sales[Amount]")
        ]);

        new ConsoleAuditRenderer(console).Render("Example.pbip", Sample(), result, 1);

        Assert.Contains("AUDIT: FAIL", console.Output);
        Assert.Contains("ERROR", console.Output);
        Assert.Contains("exit code: 1", console.Output);
    }

    [Fact]
    public void ZeroEvaluatedRulesDoesNotClaimPassingAudit()
    {
        using var console = new TestConsole();
        var result = new ModelQualityAuditResult("Demo", [], []);

        new ConsoleAuditRenderer(console).Render("Example.pbip", Sample(), result, 2);

        Assert.Contains("No rules evaluated", console.Output);
        Assert.Contains("AUDIT: NOT EVALUATED", console.Output);
        Assert.DoesNotContain("AUDIT: PASS", console.Output);
    }

    [Fact]
    public void EscapesNamesAndUntrustedFindingText()
    {
        using var console = new TestConsole();
        console.Profile.Width = 120;
        var model = Sample() with { Name = "[red]Model[/]" };
        var finding = new ModelQualityFinding(
            "SM-999", "[blue]Title[/]", DiagnosticSeverity.Warning,
            "[red]Location[/]", "Message [green] data", "Why [yellow]", "Fix [blue]");
        var result = new ModelQualityAuditResult(model.Name, ["SM-999"], [finding]);

        new ConsoleAuditRenderer(console).Render("[red]Example.pbip", model, result, 0);

        Assert.Contains("[red]Model[/]", console.Output);
        Assert.Contains("[blue]Title[/]", console.Output);
        Assert.Contains("[red]Location[/]", console.Output);
        Assert.Contains("Message [green] data", console.Output);
    }

    [Fact]
    public void NoColorModeDoesNotEmitAnsiSequences()
    {
        using var writer = new StringWriter();
        var console = ConsoleFactory.Create(writer, noColor: true);

        new ConsoleAuditRenderer(console).Render("Example.pbip", Sample(), Advisories(), 0);

        var output = writer.ToString();
        Assert.Contains("AUDIT: PASS WITH ADVISORIES", output);
        Assert.Contains("01  PROJECT PROFILE", output);
        Assert.False(console.Profile.Capabilities.Ansi);

        var escapeIndex = output.IndexOf('\u001b');
        var nearbyCodePoints = escapeIndex < 0
            ? string.Empty
            : string.Join(" ", output[
                Math.Max(0, escapeIndex - 12)..Math.Min(output.Length, escapeIndex + 28)]
                .Select(character => $"U+{(int)character:X4}"));
        Assert.True(escapeIndex < 0,
            $"Plain audit output contains ESC at index {escapeIndex}: {nearbyCodePoints}");
    }

    [Fact]
    public void NoColorModeRendersAllAdvisoryGuidanceWithoutAnsi()
    {
        using var writer = new StringWriter();
        var console = ConsoleFactory.Create(writer, noColor: true);

        new ConsoleAuditRenderer(console).Render("Example.pbip", Sample(), Advisories(), 0);

        var text = writer.ToString();
        Assert.Contains("01  PROJECT PROFILE", text);
        Assert.Contains("02  RULE COVERAGE", text);
        Assert.Contains("03  DIAGNOSTIC REPORT", text);
        Assert.Contains("04  FINAL RESULT", text);
        Assert.Contains("SM-001", text);
        Assert.Contains("SM-003", text);
        Assert.Contains("WHY IT MATTERS", text);
        Assert.Contains("HOW TO FIX", text);
        Assert.Contains("Sales[ProductId]", text);
        Assert.True(text.IndexOf('\u001b') < 0, "Plain-text audit output must not contain the ESC (U+001B) control character.");
    }

    [Fact]
    public void NoColorModeFailureDoesNotEmitAnsi()
    {
        using var writer = new StringWriter();
        var console = ConsoleFactory.Create(writer, noColor: true);

        new ConsoleAuditRenderer(console).RenderFailure(
            "Missing.pbip", "PROJECT DISCOVERY FAILED", "No PBIP found",
            "Cannot inspect project", "Provide a valid path", 2);

        var text = writer.ToString();
        Assert.Contains("AUDIT: NOT RUN", text);
        Assert.Contains("WHY IT MATTERS", text);
        Assert.Contains("HOW TO FIX", text);
        Assert.Contains("exit code: 2", text);
        Assert.True(text.IndexOf('\u001b') < 0, "Plain-text audit output must not contain the ESC (U+001B) control character.");
    }

    [Fact]
    public void NoColorModePreservesLiteralMarkupLikeMetadata()
    {
        using var writer = new StringWriter();
        var console = ConsoleFactory.Create(writer, noColor: true);
        var model = Sample() with { Name = "[red]Model[/]" };
        var findings = new ModelQualityAuditResult(model.Name, ["SM-003"],
        [
            new ModelQualityFinding("SM-003", "Review [blue]Keys[/]",
                DiagnosticSeverity.Information, "Sales[ProductId]",
                "Inspect [green] metadata", "Why [orange]", "Hide [blue]")
        ]);

        new ConsoleAuditRenderer(console).Render("Example.pbip", model, findings, 0);

        var text = writer.ToString();
        Assert.Contains("[red]Model[/]", text);
        Assert.Contains("Review [blue]Keys[/]", text);
        Assert.Contains("Inspect [green] metadata", text);
        Assert.True(text.IndexOf('\u001b') < 0, "Plain-text audit output must not contain the ESC (U+001B) control character.");
    }

    [Fact]
    public void FailureScreenIsBrandedWithGuidanceAndHonestStatus()
    {
        using var console = new TestConsole();
        console.Profile.Width = 120;

        new ConsoleAuditRenderer(console).RenderFailure(
            "[bad]MyProject.pbip", "PROJECT DISCOVERY FAILED", "No .pbip found",
            "The [model] is missing.", "Provide an existing file.", 2);

        Assert.Contains("SEMANTIC MODEL AUDIT", console.Output);
        Assert.Contains("PROJECT DISCOVERY FAILED", console.Output);
        Assert.Contains("[bad]MyProject.pbip", console.Output);
        Assert.Contains("The [model] is missing", console.Output);
        Assert.Contains("WHY IT MATTERS", console.Output);
        Assert.Contains("HOW TO FIX", console.Output);
        Assert.Contains("AUDIT: NOT RUN", console.Output);
        Assert.Contains("exit code: 2", console.Output);
    }

    [Fact]
    public void LargeFindingCollectionsAreTruncatedButTotalsRemainCorrect()
    {
        using var console = new TestConsole();
        console.Profile.Width = 120;
        var findings = Enumerable.Range(1, 34)
            .Select(number => Finding("SM-003", DiagnosticSeverity.Information, $"Column-{number:00}"))
            .ToArray();
        var result = new ModelQualityAuditResult("Demo", ["SM-003"], findings);

        new ConsoleAuditRenderer(console).Render("Example.pbip", Sample(), result, 0);

        Assert.Contains("Showing first 30 of 34", console.Output);
        Assert.DoesNotContain("Column-31", console.Output);
        Assert.Contains("34 information", console.Output);
    }

    private static TomModelSnapshot Sample() => new(
        "Demo", 1600,
        [new TomTableSnapshot("Sales", false,
            [new TomColumnSnapshot("ProductId", "Int64", "Data", false, null)],
            [], 0, 1)],
        [new TomRelationshipSnapshot("r1", "Sales[ProductId]", "Product[ProductId]", true, "OneDirection")]);

    private static ModelQualityAuditResult Advisories() => new(
        "Demo", ["SM-001", "SM-002", "SM-003", "SM-004"],
        [
            Finding("SM-001", DiagnosticSeverity.Warning, "Sales[ProductId]"),
            Finding("SM-003", DiagnosticSeverity.Information, "Sales[ProductId]")
        ]);

    private static ModelQualityFinding Finding(string code, DiagnosticSeverity severity, string location) =>
        new(code, "Example quality rule", severity, location,
            "An issue was found", "A reviewer should examine it", "Review the model object");
}

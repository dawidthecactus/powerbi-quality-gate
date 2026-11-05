using PowerBIQualityGate.Cli.Rendering;
using PowerBIQualityGate.Core.Inspection;
using PowerBIQualityGate.Core.Quality;
using PowerBIQualityGate.Core.Validation;

using Spectre.Console.Testing;

namespace PowerBIQualityGate.Tests;

public sealed class CompactAuditRendererTests
{
    [Fact]
    public void SummaryIsShortAndDoesNotExposeObjectLocations()
    {
        using var console = new TestConsole();
        console.Profile.Width = 120;
        new CompactAuditRenderer(console).Render("Sample.pbip", Model(), Findings(), Decision());

        var text = console.Output;
        Assert.Contains("PBIQ", text);
        Assert.Contains("Sample.pbip", text);
        Assert.Contains("AUDIT: PASS WITH COVERAGE GAPS", text);
        Assert.Contains("SM-003", text);
        Assert.Contains("2 findings", text);
        Assert.Contains("CHECKS", text);
        Assert.Contains("--details", text);
        Assert.False(text.Contains("Sales[ProductId]", StringComparison.Ordinal));
        Assert.False(text.Contains("WHY IT MATTERS", StringComparison.Ordinal));
        Assert.True(text.Split('\n').Length <= 20);
    }

    [Fact]
    public void DetailsShowEveryObjectWithOneRemediationPerRule()
    {
        using var console = new TestConsole();
        console.Profile.Width = 120;
        new CompactAuditRenderer(console).Render("Sample.pbip", Model(), Findings(), Decision(), AuditOutputMode.Details);

        var text = console.Output;
        Assert.Contains("FINDINGS BY RULE", text);
        Assert.Contains("Sales[ProductId]", text);
        Assert.Contains("Sales[OrderId]", text);
        Assert.Contains("SM-003  INFO (2)", text);
        Assert.Equal(1, Occurrences(text, "WHY IT MATTERS:"));
        Assert.Equal(1, Occurrences(text, "HOW TO FIX:"));
    }

    [Fact]
    public void DetailsDoNotMergeDifferentGuidanceUnderSameCode()
    {
        using var console = new TestConsole();
        console.Profile.Width = 120;
        var audit = new ModelQualityAuditResult("Demo", ["SM-003"],
        [
            new ModelQualityFinding("SM-003", "Review keys", DiagnosticSeverity.Information,
                "T[A]", "Visible", "Why A", "Fix A"),
            new ModelQualityFinding("SM-003", "Review keys", DiagnosticSeverity.Information,
                "T[B]", "Visible", "Why B", "Fix B")
        ]);
        var decision = new QualityGatePolicy().Evaluate(audit);
        new CompactAuditRenderer(console).Render("Sample.pbip", Model(), audit, decision, AuditOutputMode.Details);
        Assert.Equal(2, Occurrences(console.Output, "WHY IT MATTERS:"));
        Assert.Contains("Why A", console.Output);
        Assert.Contains("Why B", console.Output);
    }

    [Fact]
    public void SummaryEmphasizesRuleCoverageWithoutCallingItClean()
    {
        using var console = new TestConsole();
        console.Profile.Width = 120;
        new CompactAuditRenderer(console).Render("Sample.pbip", Model(), Findings(), Decision());

        var text = console.Output;
        Assert.Contains("CHECKS", text);
        Assert.Contains("SM-002", text);
        Assert.Contains("SM-002 coverage: 0.0%", text);
        Assert.Contains("(0/1 candidates)", text);
        Assert.Contains("Coverage gap", text);
        Assert.DoesNotContain("Sales[ProductId]", text);
    }

    [Fact]
    public void StandardTechnicalKeyDetailsDoNotRepeatTheColumnNameInMessage()
    {
        using var console = new TestConsole();
        console.Profile.Width = 120;
        var audit = new ModelQualityAuditResult("Demo", ["SM-003"],
        [
            new ModelQualityFinding("SM-003", "Review visible technical columns",
                DiagnosticSeverity.Information, "Sales[Customer Key]",
                "Potential technical key column 'Customer Key' is visible.", "Why", "Fix"),
            new ModelQualityFinding("SM-003", "Review visible technical columns",
                DiagnosticSeverity.Information, "Products[Product Key]",
                "Potential technical key column 'Product Key' is visible.", "Why", "Fix")
        ]);
        var decision = new QualityGatePolicy().Evaluate(audit);
        new CompactAuditRenderer(console).Render("Sample.pbip", Model(), audit, decision, AuditOutputMode.Details);

        var text = console.Output;
        Assert.Contains("Sales[Customer Key]", text);
        Assert.Contains("Products[Product Key]", text);
        Assert.DoesNotContain("Potential technical key column", text);
        Assert.Equal(1, Occurrences(text, "WHY IT MATTERS:"));
        Assert.Equal(1, Occurrences(text, "HOW TO FIX:"));
    }

    [Fact]
    public void NonRedundantTechnicalKeyMessageIsStillDisplayed()
    {
        using var console = new TestConsole();
        console.Profile.Width = 120;
        var audit = new ModelQualityAuditResult("Demo", ["SM-003"],
        [
            new ModelQualityFinding("SM-003", "Review visible technical columns",
                DiagnosticSeverity.Information, "Sales[Customer Key]",
                "Potential technical key column 'Different Key' is visible.", "Why", "Fix")
        ]);
        var decision = new QualityGatePolicy().Evaluate(audit);
        new CompactAuditRenderer(console).Render("Sample.pbip", Model(), audit, decision, AuditOutputMode.Details);

        Assert.Contains("Potential technical key column 'Different Key' is visible.", console.Output);
    }

    [Fact]
    public void QuietModeIsExactlyOneUnstyledLine()
    {
        using var console = new TestConsole();
        new CompactAuditRenderer(console).Render("secret.pbip", Model(), Findings(), Decision(), AuditOutputMode.Quiet);
        var text = console.Output;
        Assert.Single(text.TrimEnd().Split('\n'));
        Assert.Contains("AUDIT: PASS WITH COVERAGE GAPS", text);
        Assert.Contains("exit code: 0", text);
        Assert.False(text.Contains("secret.pbip", StringComparison.Ordinal));
        Assert.False(text.Contains("Sales[ProductId]", StringComparison.Ordinal));
        Assert.True(text.IndexOf('\u001b') < 0);
    }

    [Fact]
    public void PlainOutputPreservesLiteralMarkupAndHasNoAnsi()
    {
        using var writer = new StringWriter();
        var console = ConsoleFactory.Create(writer, noColor: true);
        var snapshot = Model() with { Name = "[red]Demo[/]" };
        new CompactAuditRenderer(console).Render("[blue]Report.pbip", snapshot, Findings(), Decision(), AuditOutputMode.Summary);
        var text = writer.ToString();
        Assert.Contains("[blue]Report.pbip", text);
        Assert.False(console.Profile.Capabilities.Ansi);
        Assert.True(text.IndexOf('\u001b') < 0);
        Assert.False(text.Contains("WHY IT MATTERS", StringComparison.Ordinal));
    }

    [Fact]
    public void DetailsEscapeUntrustedMarkupAndRemoveNewlinesFromMetadata()
    {
        using var console = new TestConsole();
        console.Profile.Width = 120;
        var audit = new ModelQualityAuditResult("Demo", ["SM-003"],
        [
            new ModelQualityFinding("SM-003", "[red]Title[/]", DiagnosticSeverity.Information,
                "Sales[Id]\nBROKEN", "Value [blue]test[/]\u001b[31m", "Why", "Fix")
        ]);
        var decision = new QualityGatePolicy().Evaluate(audit);
        new CompactAuditRenderer(console).Render("Sample.pbip", Model(), audit, decision, AuditOutputMode.Details);
        Assert.Contains("[red]Title[/]", console.Output);
        Assert.Contains("Sales[Id] BROKEN", console.Output);
        Assert.False(console.Output.Contains("\u001b[31m", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(QualityGateOutcome.FailedOnWarnings, "FAIL (WARNING POLICY)")]
    [InlineData(QualityGateOutcome.FailedOnCoverage, "FAIL (COVERAGE POLICY)")]
    [InlineData(QualityGateOutcome.Failed, "FAIL")]
    public void QuietModeReportsDistinctFailureReason(QualityGateOutcome outcome, string expected)
    {
        using var console = new TestConsole();
        var decision = Decision() with { Outcome = outcome, ExitCode = 1 };
        new CompactAuditRenderer(console).Render("Sample.pbip", Model(), Findings(), decision, AuditOutputMode.Quiet);
        Assert.Contains($"AUDIT: {expected}", console.Output);
        Assert.Contains("exit code: 1", console.Output);
    }

    [Fact]
    public void DefaultFailureIsBriefAndShowsNextAction()
    {
        using var console = new TestConsole();
        new CompactAuditRenderer(console).RenderFailure("private-path", "STRUCTURE VALIDATION FAILED",
            "Model cannot be validated.", "Why", "Run pbiq scan <path>", 1);
        Assert.Contains("AUDIT: NOT RUN", console.Output);
        Assert.Contains("STRUCTURE VALIDATION FAILED", console.Output);
        Assert.Contains("pbiq scan", console.Output);
        Assert.False(console.Output.Contains("private-path", StringComparison.Ordinal));
        Assert.True(console.Output.Split('\n').Length <= 7);
    }

    [Fact]
    public void QuietFailureIsOneLineAndDoesNotExposePath()
    {
        using var writer = new StringWriter();
        var console = ConsoleFactory.Create(writer, noColor: true);
        new CompactAuditRenderer(console).RenderFailure("secret", "PROJECT DISCOVERY FAILED",
            "Missing.", "Why", "Fix", 2, AuditOutputMode.Quiet);
        var text = writer.ToString();
        Assert.Single(text.TrimEnd().Split('\n'));
        Assert.Contains("exit code: 2", text);
        Assert.False(text.Contains("secret", StringComparison.Ordinal));
        Assert.True(text.IndexOf('\u001b') < 0);
    }

    [Fact]
    public void DetailsFailureKeepsWhyAndHow()
    {
        using var console = new TestConsole();
        new CompactAuditRenderer(console).RenderFailure("Sample.pbip", "TOM DESERIALIZATION FAILED",
            "Invalid TMDL.", "Model was not loaded.", "Inspect syntax.", 2, AuditOutputMode.Details);
        Assert.Contains("WHY IT MATTERS", console.Output);
        Assert.Contains("HOW TO FIX", console.Output);
        Assert.Contains("Sample.pbip", console.Output);
    }

    [Fact]
    public void SummarySaysCoverageNotApplicableWhenNoCandidates()
    {
        using var console = new TestConsole();
        var audit = new ModelQualityAuditResult("Demo", ["SM-002"], []);
        var decision = new QualityGatePolicy().Evaluate(audit);
        new CompactAuditRenderer(console).Render("Sample.pbip", Model(), audit, decision);
        Assert.Contains("SM-002 coverage: N/A", console.Output);
        Assert.Contains("AUDIT: PASS", console.Output);
    }

    private static int Occurrences(string text, string pattern) =>
        text.Split(pattern, StringSplitOptions.None).Length - 1;

    private static TomModelSnapshot Model() => new("Demo", 1600,
        [new TomTableSnapshot("Sales", false,
            [new TomColumnSnapshot("ProductId", "Int64", "Data", false, null)],
            [new TomMeasureSnapshot("Revenue", "1", null, false, "Unknown")], 0, 1)], []);

    private static ModelQualityAuditResult Findings() => new("Demo", ["SM-003"],
    [
        new ModelQualityFinding("SM-003", "Review visible technical columns",
            DiagnosticSeverity.Information, "Sales[ProductId]", "Visible technical key",
            "Surrogate keys clutter Fields", "Hide technical columns"),
        new ModelQualityFinding("SM-003", "Review visible technical columns",
            DiagnosticSeverity.Information, "Sales[OrderId]", "Visible technical key",
            "Surrogate keys clutter Fields", "Hide technical columns")
    ]);

    private static QualityGateDecision Decision() => new(
        QualityGateOutcome.PassedWithCoverageGaps, 0, false, null, 0, 1, true);
}

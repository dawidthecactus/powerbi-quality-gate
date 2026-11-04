using PowerBIQualityGate.Cli.Rendering;
using PowerBIQualityGate.Core.Discovery;
using PowerBIQualityGate.Core.Validation;

using Spectre.Console.Testing;

namespace PowerBIQualityGate.Tests;

public class ConsoleReportRendererTests
{
    [Fact]
    public void ValidProject_PrintsBrandedHeaderProfileAndCheckpoints()
    {
        using var console = new TestConsole();
        var result = Result(ReportDefinitionFormat.Pbir, SemanticModelDefinitionFormat.Tmdl);

        new ConsoleReportRenderer(console).RenderScan("Demo.pbip", result);

        Assert.Contains("PBIQ", console.Output);
        Assert.Contains("POWER BI QUALITY GATE", console.Output);
        Assert.Contains("PROJECT PROFILE", console.Output);
        Assert.Contains("SCAN CHECKPOINTS", console.Output);
        Assert.Contains("DIAGNOSTIC REPORT", console.Output);
        Assert.Contains("FINAL RESULT", console.Output);
        Assert.Contains("Demo.pbip", console.Output);
        Assert.Contains("PBIR", console.Output);
        Assert.Contains("TMDL", console.Output);
        Assert.Contains("STRUCTURE VALIDATION: PASS", console.Output);
    }

    [Fact]
    public void ValidProject_DoesNotClaimItValidatedDaxOrSchema()
    {
        using var console = new TestConsole();

        new ConsoleReportRenderer(console).RenderScan(
            "Demo.pbip", Result(ReportDefinitionFormat.Pbir, SemanticModelDefinitionFormat.Tmdl));

        Assert.Contains("No structural issues detected", console.Output);
        // Spectre.Console may wrap terminal output across multiple lines.
        // Normalize whitespace while still verifying the complete scope disclaimer.
        var normalizedOutput = string.Join(" ",
            console.Output.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        Assert.True(
            normalizedOutput.Contains(
                "No DAX, model-quality or full schema validation", StringComparison.Ordinal),
            $"Scope disclaimer missing. Rendered output tail:\n{console.Output[^Math.Min(1000, console.Output.Length)..]}");
        Assert.Contains("0 errors", console.Output);
        Assert.DoesNotContain("18 checks passed", console.Output);
    }

    [Fact]
    public void InvalidVersion_ExplainsWhyAndHowToFix()
    {
        using var console = new TestConsole();
        var result = Result(
            ReportDefinitionFormat.Unknown,
            SemanticModelDefinitionFormat.Unknown,
            new ProjectDiagnostic("PBI-002", DiagnosticSeverity.Error,
                "Expected PBIP shortcut version '1.0'.", "Broken.pbip"));

        new ConsoleReportRenderer(console).RenderScan("Broken.pbip", result);

        Assert.Contains("PBI-002", console.Output);
        Assert.Contains("WHY IT MATTERS", console.Output);
        Assert.Contains("HOW TO FIX", console.Output);
        Assert.Contains("re-saving", console.Output);
        Assert.Contains("1 error", console.Output);
        Assert.Contains("STRUCTURE VALIDATION: FAIL", console.Output);
        Assert.Contains("NOT RESOLVED", console.Output);
    }

    [Fact]
    public void InvalidProject_HasNoWideDiagnosticsTable()
    {
        using var console = new TestConsole();
        var result = Result(
            ReportDefinitionFormat.Unknown,
            SemanticModelDefinitionFormat.Unknown,
            new ProjectDiagnostic("PBI-003", DiagnosticSeverity.Error,
                "Missing report artifact.", "Broken.pbip"));

        new ConsoleReportRenderer(console).RenderScan("Broken.pbip", result);

        Assert.DoesNotContain("Issue / location", console.Output);
        Assert.DoesNotContain("Severity | Code", console.Output);
        Assert.Contains("HOW TO FIX", console.Output);
    }

    [Fact]
    public void UntrustedDiagnosticText_IsEscapedAndDisplayedLiterally()
    {
        using var console = new TestConsole();
        var result = Result(
            ReportDefinitionFormat.Unknown,
            SemanticModelDefinitionFormat.Unknown,
            new ProjectDiagnostic("PBI-999", DiagnosticSeverity.Error,
                "Bad [red] data", "[bad]/X.pbip"));

        new ConsoleReportRenderer(console).RenderScan("[bad]/X.pbip", result);

        Assert.Contains("Bad [red] data", console.Output);
        Assert.Contains("[bad]", console.Output);
        Assert.Contains("Unknown diagnostic", console.Output);
    }

    [Fact]
    public void DiscoveryFailure_ExplainsCauseAndRetainsExitCodeTwo()
    {
        using var console = new TestConsole();
        var failure = new ProjectDiscoveryResult(
            DiscoveryStatus.NotFound, null, "Project path does not exist.");

        new ConsoleReportRenderer(console).RenderDiscoveryFailure(failure, "missing/location");

        Assert.Contains("Project path does not exist", console.Output);
        Assert.Contains("Project discovery failed", console.Output);
        Assert.Contains("missing/location", console.Output);
        Assert.Contains("WHY IT MATTERS", console.Output);
        Assert.Contains("HOW TO FIX", console.Output);
        Assert.Contains("DISCOVERY: FAIL", console.Output);
        Assert.Contains("exit code: 2", console.Output);
    }

    [Fact]
    public void NoColorConsole_EmitsNoAnsiCodes()
    {
        using var writer = new StringWriter();
        var console = ConsoleFactory.Create(writer, noColor: true);

        new ConsoleReportRenderer(console).RenderScan(
            "Demo.pbip", Result(ReportDefinitionFormat.PbirLegacy, SemanticModelDefinitionFormat.External));

        var output = writer.ToString();
        Assert.Contains("PBIR-Legacy", output);
        Assert.Contains("External connection", output);
        Assert.DoesNotContain("\u001b[", output);
    }

    [Fact]
    public void Diagnostics_ArePrioritizedBySeverityThenCode()
    {
        using var console = new TestConsole();
        var result = Result(
            ReportDefinitionFormat.Unknown,
            SemanticModelDefinitionFormat.Unknown,
            new ProjectDiagnostic("PBI-011", DiagnosticSeverity.Warning, "A warning", "Demo.pbir"),
            new ProjectDiagnostic("PBI-003", DiagnosticSeverity.Error, "First error", "Demo.pbip"),
            new ProjectDiagnostic("PBI-002", DiagnosticSeverity.Error, "Second error", "Demo.pbip"));

        new ConsoleReportRenderer(console).RenderScan("Demo.pbip", result);

        Assert.True(console.Output.IndexOf("PBI-002", StringComparison.Ordinal) <
                    console.Output.IndexOf("PBI-003", StringComparison.Ordinal));
        Assert.True(console.Output.IndexOf("PBI-003", StringComparison.Ordinal) <
                    console.Output.IndexOf("PBI-011", StringComparison.Ordinal));
        Assert.Contains("2 errors", console.Output);
        Assert.Contains("1 warning", console.Output);
        Assert.DoesNotContain("1 warnings", console.Output);
    }

    [Fact]
    public void DisplayingDiagnostics_DoesNotMutateTheResult()
    {
        using var console = new TestConsole();
        var diagnostic = new ProjectDiagnostic("PBI-017", DiagnosticSeverity.Error,
            "Missing TMDL file", "model.tmdl");
        var result = Result(ReportDefinitionFormat.Pbir, SemanticModelDefinitionFormat.Tmdl, diagnostic);

        new ConsoleReportRenderer(console).RenderScan("Demo.pbip", result);

        Assert.Single(result.Diagnostics);
        Assert.Same(diagnostic, result.Diagnostics[0]);
    }

    [Theory]
    [InlineData("PBI-001")]
    [InlineData("PBI-002")]
    [InlineData("PBI-003")]
    [InlineData("PBI-004")]
    [InlineData("PBI-005")]
    [InlineData("PBI-006")]
    [InlineData("PBI-007")]
    [InlineData("PBI-008")]
    [InlineData("PBI-009")]
    [InlineData("PBI-010")]
    [InlineData("PBI-011")]
    [InlineData("PBI-012")]
    [InlineData("PBI-013")]
    [InlineData("PBI-014")]
    [InlineData("PBI-015")]
    [InlineData("PBI-016")]
    [InlineData("PBI-017")]
    [InlineData("PBI-018")]
    public void EveryImplementedDiagnostic_HasAnExplanationAndFix(string code)
    {
        var advice = DiagnosticGuidance.ForCode(code);

        Assert.False(string.IsNullOrWhiteSpace(advice.Why));
        Assert.False(string.IsNullOrWhiteSpace(advice.Fix));
        Assert.NotEqual("Unknown diagnostic", advice.Area);
    }

    private static ProjectStructureValidationResult Result(
        ReportDefinitionFormat report,
        SemanticModelDefinitionFormat model,
        params ProjectDiagnostic[] diagnostics) => new(
        ReportDirectory: null,
        ReportFormat: report,
        SemanticModelDirectory: null,
        SemanticModelFormat: model,
        Diagnostics: diagnostics);
}

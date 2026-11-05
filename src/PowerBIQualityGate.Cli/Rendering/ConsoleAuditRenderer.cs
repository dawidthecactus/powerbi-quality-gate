using PowerBIQualityGate.Core.Inspection;
using PowerBIQualityGate.Core.Quality;
using PowerBIQualityGate.Core.Validation;

using Spectre.Console;

namespace PowerBIQualityGate.Cli.Rendering;

/// <summary>Read-only, branded presentation of semantic-model quality findings.</summary>
public sealed class ConsoleAuditRenderer
{
    private const string Red = "#FF7373";
    private const string Amber = "#F6CD71";
    private const int MaxFindings = 30;

    private readonly IAnsiConsole _console;

    public ConsoleAuditRenderer(IAnsiConsole console)
    {
        _console = console ?? throw new ArgumentNullException(nameof(console));
    }

    // Compatibility overload for existing render tests and callers.
    public void Render(string pbipPath, TomModelSnapshot snapshot, ModelQualityAuditResult result, int exitCode)
    {
        var decision = new QualityGatePolicy().Evaluate(result);
        if (decision.ExitCode != exitCode)
        {
            throw new ArgumentException("Exit code does not match the default quality gate policy.", nameof(exitCode));
        }

        Render(pbipPath, snapshot, result, decision);
    }

    public void Render(
        string pbipPath,
        TomModelSnapshot snapshot,
        ModelQualityAuditResult result,
        QualityGateDecision decision)
    {
        ArgumentNullException.ThrowIfNull(pbipPath);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(decision);

        // Plain/redirected output bypasses styled Spectre renderables entirely.
        // The --no-color contract is plain text, including for individual findings.
        if (!_console.Profile.Capabilities.Ansi)
        {
            new PlainTextAuditRenderer(_console.Profile.Out.Writer)
                .Render(pbipPath, snapshot, result, decision);
            return;
        }

        PbiqConsoleChrome.WriteBrand(_console, "SEMANTIC MODEL AUDIT / RULE PACK 01");
        WriteProfile(pbipPath, snapshot);
        WriteCoverage(snapshot, result, decision);
        WriteFindings(result);
        WriteFinalResult(result, decision);
    }

    public void RenderFailure(
        string inputPath,
        string title,
        string message,
        string why,
        string fix,
        int exitCode)
    {
        if (!_console.Profile.Capabilities.Ansi)
        {
            new PlainTextAuditRenderer(_console.Profile.Out.Writer)
                .RenderFailure(inputPath, title, message, why, fix, exitCode);
            return;
        }

        PbiqConsoleChrome.WriteBrand(_console, "SEMANTIC MODEL AUDIT / RULE PACK 01");
        PbiqConsoleChrome.WriteSection(_console, "01", "PROJECT PROFILE");
        _console.MarkupLine($"  [{PbiqConsoleChrome.Muted}]Requested path[/]  {Markup.Escape(inputPath ?? string.Empty)}");
        _console.WriteLine();

        PbiqConsoleChrome.WriteSection(_console, "02", "DIAGNOSTIC REPORT");
        _console.MarkupLine($"  [bold {Red}]✕  {Markup.Escape(title)}[/]");
        _console.MarkupLine($"  {Markup.Escape(message)}");
        _console.WriteLine();
        WriteGuidance(why, fix);

        PbiqConsoleChrome.WriteSection(_console, "03", "FINAL RESULT");
        _console.Write(new Panel(new Markup(
            $"[bold {Red}]✕  AUDIT: NOT RUN[/]\n" +
            $"[{PbiqConsoleChrome.Muted}]0 rules evaluated  ·  exit code: {exitCode}[/]"))
            .RoundedBorder()
            .BorderColor(Color.FromHex(Red)));
        _console.MarkupLine($"  [{PbiqConsoleChrome.Muted}]No semantic-model quality verdict was produced.[/]");
    }

    private void WriteProfile(string pbipPath, TomModelSnapshot snapshot)
    {
        PbiqConsoleChrome.WriteSection(_console, "01", "PROJECT PROFILE");
        var content = new Markup(
            $"[bold {PbiqConsoleChrome.Ice}]{Markup.Escape(Path.GetFileName(pbipPath))}[/]\n" +
            $"[{PbiqConsoleChrome.Muted}]Model[/]         {Markup.Escape(snapshot.Name)}\n" +
            $"[{PbiqConsoleChrome.Muted}]Definition[/]    [bold {PbiqConsoleChrome.Mint}]TMDL / Microsoft TOM[/]\n" +
            $"[{PbiqConsoleChrome.Muted}]Compatibility[/] {snapshot.CompatibilityLevel}\n" +
            $"[{PbiqConsoleChrome.Muted}]Scope[/]         Metadata-only, offline audit");
        _console.Write(new Panel(content)
            .Header("[bold]POWER BI PROJECT[/]")
            .RoundedBorder()
            .BorderColor(Color.FromHex(PbiqConsoleChrome.Border)));
        _console.WriteLine();
    }

    private void WriteCoverage(TomModelSnapshot snapshot, ModelQualityAuditResult result, QualityGateDecision decision)
    {
        PbiqConsoleChrome.WriteSection(_console, "02", "RULE COVERAGE");
        _console.MarkupLine(
            $"  [bold {PbiqConsoleChrome.Mint}]{result.RulesEvaluated}[/] rules evaluated    " +
            $"[bold {Red}]{Count(result.ErrorCount, "error", "errors")}[/]    " +
            $"[bold {Amber}]{Count(result.WarningCount, "warning", "warnings")}[/]    " +
            $"[{PbiqConsoleChrome.Muted}]{result.InformationCount} information[/]");
        _console.MarkupLine(
            $"  [{PbiqConsoleChrome.Muted}]Rule codes[/]  {Markup.Escape(string.Join("  ·  ", result.EvaluatedRuleCodes))}");
        var policy = decision.FailOnWarning
            ? "Active policy: warnings block the audit; information does not."
            : "Default policy: warnings and information do not fail the audit.";
        _console.MarkupLine($"  [{PbiqConsoleChrome.Muted}]{policy}[/]");
        WriteObjectCoverage(snapshot, result);
        _console.WriteLine();
    }

    private void WriteObjectCoverage(TomModelSnapshot snapshot, ModelQualityAuditResult result)
    {
        var coverage = ModelAuditCoverageAnalyzer.Analyze(snapshot, result);
        if (coverage.Rules.Count == 0)
        {
            return;
        }

        _console.WriteLine();
        _console.MarkupLine($"  [bold {PbiqConsoleChrome.Ice}]OBJECT COVERAGE[/]  [{PbiqConsoleChrome.Muted}](metadata predicates, not DAX evaluation)[/]");
        foreach (var scope in coverage.Rules)
        {
            switch (scope.RuleCode)
            {
                case "SM-001":
                    _console.MarkupLine($"  [bold {PbiqConsoleChrome.Teal}]SM-001[/]  {scope.Checked}/{scope.Total} active relationships checked; {scope.Skipped} inactive excluded");
                    break;
                case "SM-002":
                    WriteMeasureCoverage(coverage.MeasureFormatting!);
                    break;
                case "SM-003":
                    _console.MarkupLine($"  [bold {PbiqConsoleChrome.Teal}]SM-003[/]  {scope.Checked}/{scope.Total} visible columns checked; {scope.Skipped} hidden excluded");
                    break;
                case "SM-004":
                    _console.MarkupLine($"  [bold {PbiqConsoleChrome.Teal}]SM-004[/]  {scope.Checked}/{scope.Total} inactive relationships checked; {scope.Skipped} active excluded");
                    break;
            }
        }

        _console.MarkupLine($"  [{PbiqConsoleChrome.Muted}]Coverage is based on TOM metadata; unknown measure types are not inferred from DAX.[/]");
    }

    private void WriteMeasureCoverage(MeasureFormattingCoverage measures)
    {
        _console.MarkupLine($"  [bold {PbiqConsoleChrome.Teal}]SM-002[/]  {measures.CheckedNumeric}/{measures.Visible} visible measures checked for numeric formatting ({measures.Total} total)");
        _console.MarkupLine($"          formatted {measures.Formatted} (static {measures.FormattedStatic}, dynamic {measures.FormattedDynamic}); unformatted {measures.Unformatted}");
        _console.MarkupLine($"          excluded: unknown/variant/other type {measures.UnknownType}; nonnumeric {measures.NonNumeric}; hidden {measures.Hidden}");
    }

    private void WriteFindings(ModelQualityAuditResult result)
    {
        PbiqConsoleChrome.WriteSection(_console, "03", "DIAGNOSTIC REPORT");
        if (result.RulesEvaluated == 0)
        {
            _console.MarkupLine($"  [bold {Amber}]No rules evaluated. No quality verdict is available.[/]");
            _console.WriteLine();
            return;
        }

        if (result.Findings.Count == 0)
        {
            _console.MarkupLine($"  [bold {PbiqConsoleChrome.Mint}]✓  No findings reported by evaluated rules[/]");
            _console.MarkupLine($"  [{PbiqConsoleChrome.Muted}]Other modeling problems may exist outside this rule pack.[/]");
            _console.WriteLine();
            return;
        }

        foreach (var finding in result.Findings.Take(MaxFindings))
        {
            var (color, symbol, severity) = finding.Severity switch
            {
                DiagnosticSeverity.Error => (Red, "✕", "ERROR"),
                DiagnosticSeverity.Warning => (Amber, "!", "WARNING"),
                _ => (PbiqConsoleChrome.Teal, "i", "INFO")
            };

            _console.MarkupLine($"  [bold {color}]{symbol}  {severity}  {Markup.Escape(finding.RuleCode)}[/]  [bold {PbiqConsoleChrome.Ice}]{Markup.Escape(finding.RuleTitle)}[/]");
            _console.MarkupLine($"     [bold]{Markup.Escape(finding.Message)}[/]");
            _console.MarkupLine($"     [{PbiqConsoleChrome.Muted}]Object: {Markup.Escape(finding.Location)}[/]");
            _console.WriteLine();
            WriteGuidance(finding.WhyItMatters, finding.HowToFix);
        }

        if (result.Findings.Count > MaxFindings)
        {
            _console.MarkupLine(
                $"  [{PbiqConsoleChrome.Muted}]Showing first {MaxFindings} of {result.Findings.Count} findings. Totals include all findings.[/]");
            _console.WriteLine();
        }
    }

    private void WriteGuidance(string why, string fix)
    {
        _console.MarkupLine($"     [bold {Amber}]WHY IT MATTERS[/]");
        _console.MarkupLine($"     {Markup.Escape(why)}");
        _console.WriteLine();
        _console.MarkupLine($"     [bold {PbiqConsoleChrome.Teal}]HOW TO FIX[/]");
        _console.MarkupLine($"     {Markup.Escape(fix)}");
        _console.WriteLine();
    }

    private static string Count(int amount, string singular, string plural) =>
        $"{amount} {(amount == 1 ? singular : plural)}";

    private void WriteFinalResult(ModelQualityAuditResult result, QualityGateDecision decision)
    {
        PbiqConsoleChrome.WriteSection(_console, "04", "FINAL RESULT");
        var (color, status, symbol) = decision.Outcome switch
        {
            QualityGateOutcome.Failed => (Red, "AUDIT: FAIL", "✕"),
            QualityGateOutcome.FailedOnWarnings => (Red, "AUDIT: FAIL (WARNING POLICY)", "✕"),
            QualityGateOutcome.PassedWithAdvisories => (Amber, "AUDIT: PASS WITH ADVISORIES", "!"),
            QualityGateOutcome.Passed => (PbiqConsoleChrome.Mint, "AUDIT: PASS", "✓"),
            _ => (Amber, "AUDIT: NOT EVALUATED", "!")
        };

        var summary = new Markup(
            $"[bold {color}]{symbol}  {status}[/]\n" +
            $"[bold {Red}]{Count(result.ErrorCount, "error", "errors")}[/]   " +
            $"[bold {Amber}]{Count(result.WarningCount, "warning", "warnings")}[/]   " +
            $"[{PbiqConsoleChrome.Muted}]{result.InformationCount} information[/]\n" +
            $"[{PbiqConsoleChrome.Muted}]{result.RulesEvaluated} checks evaluated  ·  exit code: {decision.ExitCode}[/]");
        _console.Write(new Panel(summary)
            .RoundedBorder()
            .BorderColor(Color.FromHex(color)));
        _console.MarkupLine($"  [{PbiqConsoleChrome.Muted}]Scope: metadata-only advisory rules; not a full model or DAX validation.[/]");
        var policyNote = decision.FailOnWarning
            ? "Active policy: warnings block this run; information does not."
            : "Warnings and information do not block the default quality gate.";
        _console.MarkupLine($"  [{PbiqConsoleChrome.Muted}]{policyNote}[/]");
    }
}

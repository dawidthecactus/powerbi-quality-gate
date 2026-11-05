using System.Globalization;

using PowerBIQualityGate.Core.Inspection;
using PowerBIQualityGate.Core.Quality;
using PowerBIQualityGate.Core.Validation;

using Spectre.Console;

namespace PowerBIQualityGate.Cli.Rendering;

/// <summary>Controls verbosity without changing audit, policy or exit code semantics.</summary>
public enum AuditOutputMode
{
    Summary,
    Details,
    Quiet
}

/// <summary>
/// A compact, high-contrast terminal report. The summary has a clear result
/// hierarchy, details share remediation per rule, and quiet stays machine-safe.
/// No audit, gate or exit-code semantics are changed by the visual treatment.
/// </summary>
public sealed class CompactAuditRenderer
{
    private const string Mint = "#6FE3B5";
    private const string Amber = "#F6CD71";
    private const string Red = "#FF7373";
    private const string Ice = "#E9F1F5";
    private const string Teal = "#74BAD7";
    private const string Muted = "#93A8B7";
    private const string Border = "#455662";
    private const string Separator = "────────────────────────────────────────────────────────";

    private readonly IAnsiConsole _console;

    public CompactAuditRenderer(IAnsiConsole console)
    {
        _console = console ?? throw new ArgumentNullException(nameof(console));
    }

    public void Render(string pbipPath, TomModelSnapshot snapshot, ModelQualityAuditResult result,
        QualityGateDecision decision, AuditOutputMode mode = AuditOutputMode.Summary)
    {
        ArgumentNullException.ThrowIfNull(pbipPath);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(decision);

        var status = Status(decision.Outcome);
        if (mode == AuditOutputMode.Quiet)
        {
            // One line, no ANSI, no model names or file paths, no behavior change.
            _console.Profile.Out.Writer.WriteLine(
                $"PBIQ | AUDIT: {status} | {result.ErrorCount} errors, {result.WarningCount} warnings, {result.InformationCount} information | exit code: {decision.ExitCode}");
            return;
        }

        var color = decision.ExitCode == 1 ? Red :
            decision.HasMeasureCoverageGaps || result.WarningCount > 0 ? Amber : Mint;
        var filename = Clean(Path.GetFileName(pbipPath));

        Write("PBIQ  /  SEMANTIC MODEL AUDIT", $"[bold {Mint}]PBIQ[/]  [{Muted}]/  SEMANTIC MODEL AUDIT[/]");
        Write(Separator, $"[{Border}]{Separator}[/]");
        Write($"  {filename}", $"  [bold {Ice}]{Markup.Escape(filename)}[/]");
        Write($"  {snapshot.Tables.Count} tables  ·  {snapshot.Measures} measures  ·  {snapshot.Relationships.Count} relationships",
            $"  [{Muted}]{snapshot.Tables.Count} tables  ·  {snapshot.Measures} measures  ·  {snapshot.Relationships.Count} relationships[/]");
        Write(string.Empty);

        Write($"  ▍ AUDIT: {status}  ·  exit code: {decision.ExitCode}",
            $"  [bold {color}]▍ AUDIT: {status}[/]  [{Muted}]exit code: {decision.ExitCode}[/]");
        Write($"    {Count(result.ErrorCount, "error", "errors")}  ·  {Count(result.WarningCount, "warning", "warnings")}  ·  {result.InformationCount} information  ·  {result.RulesEvaluated} rules",
            $"    [{Muted}]{Count(result.ErrorCount, "error", "errors")}  ·  {Count(result.WarningCount, "warning", "warnings")}  ·  {result.InformationCount} information  ·  {result.RulesEvaluated} rules[/]");
        Write(string.Empty);

        Write("  CHECKS", $"  [bold {Mint}]CHECKS[/]");
        WriteRuleRows(result, decision);

        if (decision.CandidateMeasures > 0)
        {
            var percent = Percent(decision.CheckedMeasures, decision.CandidateMeasures);
            Write(string.Empty);
            Write($"  SM-002 coverage: {percent}% ({decision.CheckedMeasures}/{decision.CandidateMeasures} candidates)  ·  {decision.CandidateMeasures - decision.CheckedMeasures} unclassified",
                $"  [bold {(decision.HasMeasureCoverageGaps ? Amber : Mint)}]SM-002 coverage: {percent}%[/]  [{Muted}]({decision.CheckedMeasures}/{decision.CandidateMeasures} candidates)  ·  {decision.CandidateMeasures - decision.CheckedMeasures} unclassified[/]");
        }
        else if (result.EvaluatedRuleCodes.Contains("SM-002", StringComparer.OrdinalIgnoreCase))
        {
            Write(string.Empty);
            Write("  SM-002 coverage: N/A (no eligible measures)", $"  [{Muted}]SM-002 coverage: N/A (no eligible measures)[/]");
        }

        if (decision.MinimumMeasureCoverage is int minimum)
        {
            Write($"  Coverage policy: minimum {minimum}%", $"  [{Muted}]Coverage policy: minimum {minimum}%[/]");
        }
        if (decision.FailOnWarning)
        {
            Write("  Active policy: warnings block the audit", $"  [{Muted}]Active policy: warnings block the audit[/]");
        }
        if (decision.HasMeasureCoverageGaps)
        {
            Write("  Coverage gap: formatting not checked for unclassified measure types",
                $"  [bold {Amber}]![/] [{Muted}]Coverage gap: formatting not checked for unclassified measure types[/]");
        }

        if (mode == AuditOutputMode.Details)
        {
            WriteGroupedFindings(result);
        }
        else if (result.Findings.Count > 0)
        {
            Write("  Tip: audit <path> --details  ·  inspect findings and guidance",
                $"  [{Muted}]Tip: audit <path> --details  ·  inspect findings and guidance[/]");
        }
    }

    public void RenderFailure(string inputPath, string title, string message, string why, string fix,
        int exitCode, AuditOutputMode mode = AuditOutputMode.Summary)
    {
        if (mode == AuditOutputMode.Quiet)
        {
            _console.Profile.Out.Writer.WriteLine(
                $"PBIQ | AUDIT: NOT RUN | {Clean(title)} | exit code: {exitCode}");
            return;
        }

        Write("PBIQ  /  SEMANTIC MODEL AUDIT", $"[bold {Mint}]PBIQ[/]  [{Muted}]/  SEMANTIC MODEL AUDIT[/]");
        Write($"AUDIT: NOT RUN  |  {Clean(title)}  |  exit code: {exitCode}",
            $"[bold {Red}]AUDIT: NOT RUN[/]  [bold]{Markup.Escape(Clean(title))}[/]  [{Muted}]exit code: {exitCode}[/]");
        Write($"Reason: {Clean(message)}");
        Write($"Next: {Clean(fix)}");
        if (mode == AuditOutputMode.Details)
        {
            Write($"Requested path: {Clean(inputPath)}");
            Write($"WHY IT MATTERS: {Clean(why)}");
            Write($"HOW TO FIX: {Clean(fix)}");
        }
    }

    private void WriteRuleRows(ModelQualityAuditResult result, QualityGateDecision decision)
    {
        foreach (var code in result.EvaluatedRuleCodes)
        {
            var findings = result.Findings.Where(f => string.Equals(f.RuleCode, code, StringComparison.OrdinalIgnoreCase)).ToArray();
            var gap = string.Equals(code, "SM-002", StringComparison.OrdinalIgnoreCase) && decision.HasMeasureCoverageGaps;
            var label = code.ToUpperInvariant() switch
            {
                "SM-001" => "Relationship direction",
                "SM-002" => "Measure formatting",
                "SM-003" => "Technical columns",
                "SM-004" => "Inactive relationships",
                _ => "Model check"
            };
            var summary = string.Equals(code, "SM-002", StringComparison.OrdinalIgnoreCase) && decision.CandidateMeasures > 0
                ? $"{decision.CheckedMeasures}/{decision.CandidateMeasures} checked"
                : Count(findings.Length, "finding", "findings");
            var hasErrors = findings.Any(f => f.Severity == DiagnosticSeverity.Error);
            var hasWarnings = findings.Any(f => f.Severity == DiagnosticSeverity.Warning);
            var symbol = hasErrors ? "x" : hasWarnings || gap ? "!" : findings.Length > 0 ? "i" : "✓";
            var tone = hasErrors ? Red : hasWarnings || gap ? Amber : findings.Length > 0 ? Teal : Mint;
            var safeCode = Clean(code);
            Write($"    {symbol}  {safeCode}  {label.PadRight(23)} {summary}",
                $"    [bold {tone}]{symbol}[/]  [{Muted}]{Markup.Escape(safeCode)}[/]  {label.PadRight(23)} [bold {tone}]{summary}[/]");
        }
    }

    private void WriteGroupedFindings(ModelQualityAuditResult result)
    {
        Write(string.Empty);
        Write("  FINDINGS BY RULE", $"  [bold {Mint}]FINDINGS BY RULE[/]");
        if (result.Findings.Count == 0)
        {
            Write("  No findings from evaluated rules.");
            return;
        }

        // Grouping by text prevents different remediation from being merged.
        var groups = result.Findings.GroupBy(f => new
        {
            f.RuleCode,
            f.RuleTitle,
            f.Severity,
            f.WhyItMatters,
            f.HowToFix
        });

        foreach (var group in groups)
        {
            var severity = group.Key.Severity switch
            {
                DiagnosticSeverity.Error => "ERROR",
                DiagnosticSeverity.Warning => "WARNING",
                _ => "INFO"
            };
            var color = group.Key.Severity switch
            {
                DiagnosticSeverity.Error => Red,
                DiagnosticSeverity.Warning => Amber,
                _ => Teal
            };
            Write(string.Empty);
            var heading = $"{Clean(group.Key.RuleCode)}  {severity} ({group.Count()})  {Clean(group.Key.RuleTitle)}";
            Write(heading, $"[bold {color}]{Markup.Escape(heading)}[/]");

            // SM-003 messages repeat the column name already present in the
            // location. Collapse only the known, exact template; leave custom
            // messages and other rules fully intact.
            var isStandardTechnicalKeyGroup =
                string.Equals(group.Key.RuleCode, "SM-003", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(group.Key.RuleTitle, "Review visible technical columns", StringComparison.Ordinal) &&
                group.All(IsRedundantTechnicalKeyMessage);

            foreach (var finding in group)
            {
                var item = isStandardTechnicalKeyGroup
                    ? $"  - {Clean(finding.Location)}"
                    : $"  - {Clean(finding.Location)}: {Clean(finding.Message)}";
                Write(item);
            }
            Write($"  WHY IT MATTERS: {Clean(group.Key.WhyItMatters)}");
            Write($"  HOW TO FIX: {Clean(group.Key.HowToFix)}");
        }
    }

    private static bool IsRedundantTechnicalKeyMessage(ModelQualityFinding finding)
    {
        const string prefix = "Potential technical key column '";
        const string suffix = "' is visible.";
        if (!finding.Message.StartsWith(prefix, StringComparison.Ordinal) ||
            !finding.Message.EndsWith(suffix, StringComparison.Ordinal))
        {
            return false;
        }

        var columnName = finding.Message[prefix.Length..^suffix.Length];
        return finding.Location.EndsWith($"[{columnName}]", StringComparison.Ordinal);
    }

    private void Write(string plain, string? markup = null)
    {
        if (plain.Length == 0 && markup is null)
        {
            _console.WriteLine();
            return;
        }
        if (_console.Profile.Capabilities.Ansi)
        {
            _console.MarkupLine(markup ?? Markup.Escape(plain));
        }
        else
        {
            _console.Profile.Out.Writer.WriteLine(plain);
        }
    }

    private static string Status(QualityGateOutcome outcome) => outcome switch
    {
        QualityGateOutcome.Passed => "PASS",
        QualityGateOutcome.PassedWithAdvisories => "PASS WITH ADVISORIES",
        QualityGateOutcome.PassedWithCoverageGaps => "PASS WITH COVERAGE GAPS",
        QualityGateOutcome.Failed => "FAIL",
        QualityGateOutcome.FailedOnWarnings => "FAIL (WARNING POLICY)",
        QualityGateOutcome.FailedOnCoverage => "FAIL (COVERAGE POLICY)",
        _ => "NOT EVALUATED"
    };

    private static string Percent(int checkedCount, int candidates) =>
        (100m * checkedCount / candidates).ToString("F1", CultureInfo.InvariantCulture);

    private static string Count(int value, string singular, string plural) =>
        $"{value} {(value == 1 ? singular : plural)}";

    private static string Clean(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        // Terminal-safe single line, without escape and other control chars.
        return new string(value.Select(c => char.IsControl(c) ? ' ' : c).ToArray());
    }
}

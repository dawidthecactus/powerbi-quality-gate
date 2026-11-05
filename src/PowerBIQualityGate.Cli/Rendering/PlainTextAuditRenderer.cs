using System.Globalization;

using PowerBIQualityGate.Core.Inspection;
using PowerBIQualityGate.Core.Quality;
using PowerBIQualityGate.Core.Validation;

namespace PowerBIQualityGate.Cli.Rendering;

/// <summary>
/// Deterministic, escape-free audit output for --no-color and redirected streams.
/// This deliberately does not invoke Spectre.Console markup/renderables:
/// disabling ANSI capabilities alone is not a sufficient output contract.
/// </summary>
internal sealed class PlainTextAuditRenderer
{
    private const int MaxFindings = 30;

    private readonly TextWriter _writer;

    public PlainTextAuditRenderer(TextWriter writer)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
    }

    public void Render(string pbipPath, TomModelSnapshot snapshot, ModelQualityAuditResult result, QualityGateDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);
        WriteBrand();
        WriteSection("01", "PROJECT PROFILE");
        _writer.WriteLine($"  Project: {Path.GetFileName(pbipPath)}");
        _writer.WriteLine($"  Model: {snapshot.Name}");
        _writer.WriteLine("  Definition: TMDL / Microsoft TOM");
        _writer.WriteLine($"  Compatibility: {snapshot.CompatibilityLevel}");
        _writer.WriteLine("  Scope: Metadata-only, offline audit");
        _writer.WriteLine();

        WriteSection("02", "RULE COVERAGE");
        _writer.WriteLine($"  {result.RulesEvaluated} rules evaluated  |  {Count(result.ErrorCount, "error", "errors")}  |  {Count(result.WarningCount, "warning", "warnings")}  |  {result.InformationCount} information");
        _writer.WriteLine($"  Rule codes: {string.Join("  /  ", result.EvaluatedRuleCodes)}");
        _writer.WriteLine(decision.FailOnWarning
            ? "  Active policy: warnings block the audit; information does not."
            : "  Default policy: warnings and information do not fail the audit.");
        WriteObjectCoverage(snapshot, result);
        WriteCoveragePolicy(decision);
        _writer.WriteLine();

        WriteSection("03", "DIAGNOSTIC REPORT");
        if (result.RulesEvaluated == 0)
        {
            _writer.WriteLine("  No rules evaluated. No quality verdict is available.");
            _writer.WriteLine();
        }
        else if (result.Findings.Count == 0)
        {
            _writer.WriteLine("  No findings reported by evaluated rules");
            _writer.WriteLine("  Other modeling problems may exist outside this rule pack.");
            _writer.WriteLine();
        }
        else
        {
            foreach (var finding in result.Findings.Take(MaxFindings))
            {
                var severity = finding.Severity switch
                {
                    DiagnosticSeverity.Error => "ERROR",
                    DiagnosticSeverity.Warning => "WARNING",
                    _ => "INFO"
                };

                _writer.WriteLine($"  {severity}  {finding.RuleCode}  {finding.RuleTitle}");
                _writer.WriteLine($"     {finding.Message}");
                _writer.WriteLine($"     Object: {finding.Location}");
                _writer.WriteLine();
                WriteGuidance(finding.WhyItMatters, finding.HowToFix);
            }

            if (result.Findings.Count > MaxFindings)
            {
                _writer.WriteLine($"  Showing first {MaxFindings} of {result.Findings.Count} findings. Totals include all findings.");
                _writer.WriteLine();
            }
        }

        WriteSection("04", "FINAL RESULT");
        var verdict = decision.Outcome switch
        {
            QualityGateOutcome.Failed => "AUDIT: FAIL",
            QualityGateOutcome.FailedOnWarnings => "AUDIT: FAIL (WARNING POLICY)",
            QualityGateOutcome.FailedOnCoverage => "AUDIT: FAIL (COVERAGE POLICY)",
            QualityGateOutcome.PassedWithCoverageGaps => "AUDIT: PASS WITH COVERAGE GAPS",
            QualityGateOutcome.PassedWithAdvisories => "AUDIT: PASS WITH ADVISORIES",
            QualityGateOutcome.Passed => "AUDIT: PASS",
            _ => "AUDIT: NOT EVALUATED"
        };

        _writer.WriteLine($"  {verdict}");
        _writer.WriteLine($"  {Count(result.ErrorCount, "error", "errors")}  |  {Count(result.WarningCount, "warning", "warnings")}  |  {result.InformationCount} information");
        _writer.WriteLine($"  {result.RulesEvaluated} checks evaluated  |  exit code: {decision.ExitCode}");
        _writer.WriteLine("  Scope: metadata-only advisory rules; not a full model or DAX validation.");
        _writer.WriteLine(decision.FailOnWarning
            ? "  Active policy: warnings block this run; information does not."
            : "  Warnings and information do not block the default quality gate.");
        if (decision.HasMeasureCoverageGaps)
        {
            _writer.WriteLine("  Coverage gap: unclassified SM-002 measures were not checked for formatting.");
        }
    }

    private void WriteObjectCoverage(TomModelSnapshot snapshot, ModelQualityAuditResult result)
    {
        var coverage = ModelAuditCoverageAnalyzer.Analyze(snapshot, result);
        if (coverage.Rules.Count == 0)
        {
            return;
        }

        _writer.WriteLine();
        _writer.WriteLine("  OBJECT COVERAGE (metadata predicates, not DAX evaluation)");
        foreach (var scope in coverage.Rules)
        {
            switch (scope.RuleCode)
            {
                case "SM-001":
                    _writer.WriteLine($"  SM-001  {scope.Checked}/{scope.Total} active relationships checked; {scope.Skipped} inactive excluded");
                    break;
                case "SM-002":
                    WriteMeasureCoverage(coverage.MeasureFormatting!);
                    break;
                case "SM-003":
                    _writer.WriteLine($"  SM-003  {scope.Checked}/{scope.Total} visible columns checked; {scope.Skipped} hidden excluded");
                    break;
                case "SM-004":
                    _writer.WriteLine($"  SM-004  {scope.Checked}/{scope.Total} inactive relationships checked; {scope.Skipped} active excluded");
                    break;
            }
        }

        _writer.WriteLine("  Coverage is based on TOM metadata; unknown measure types are not inferred from DAX.");
    }

    private void WriteCoveragePolicy(QualityGateDecision decision)
    {
        if (decision.CandidateMeasures == 0)
        {
            if (decision.MinimumMeasureCoverage is not null)
            {
                _writer.WriteLine("  Measure coverage gate: N/A (no eligible visible measures).");
            }
            return;
        }

        var actual = 100m * decision.CheckedMeasures / decision.CandidateMeasures;
        _writer.WriteLine($"  SM-002 coverage gate: {actual.ToString("F1", CultureInfo.InvariantCulture)}% ({decision.CheckedMeasures}/{decision.CandidateMeasures} candidates); " +
            (decision.MinimumMeasureCoverage is int threshold
                ? $"minimum {threshold}%"
                : "not enforced"));
    }

    private void WriteMeasureCoverage(MeasureFormattingCoverage measures)
    {
        _writer.WriteLine($"  SM-002  {measures.CheckedNumeric}/{measures.Visible} visible measures checked for numeric formatting ({measures.Total} total)");
        _writer.WriteLine($"          formatted {measures.Formatted} (static {measures.FormattedStatic}, dynamic {measures.FormattedDynamic}); unformatted {measures.Unformatted}");
        _writer.WriteLine($"          excluded: unknown/variant/other type {measures.UnknownType}; nonnumeric {measures.NonNumeric}; hidden {measures.Hidden}");
    }

    public void RenderFailure(string inputPath, string title, string message, string why, string fix, int exitCode)
    {
        WriteBrand();
        WriteSection("01", "PROJECT PROFILE");
        _writer.WriteLine($"  Requested path: {inputPath}");
        _writer.WriteLine();

        WriteSection("02", "DIAGNOSTIC REPORT");
        _writer.WriteLine($"  {title}");
        _writer.WriteLine($"  {message}");
        _writer.WriteLine();
        WriteGuidance(why, fix);

        WriteSection("03", "FINAL RESULT");
        _writer.WriteLine("  AUDIT: NOT RUN");
        _writer.WriteLine($"  0 rules evaluated  |  exit code: {exitCode}");
        _writer.WriteLine("  No semantic-model quality verdict was produced.");
    }

    private void WriteBrand()
    {
        _writer.WriteLine("  ██████╗  ██████╗ ██╗ ██████╗");
        _writer.WriteLine("  ██╔══██╗██╔══██╗██║██╔═══██╗");
        _writer.WriteLine("  ██████╔╝██████╔╝██║██║   ██║");
        _writer.WriteLine("  ██╔═══╝ ██╔══██╗██║██║▄▄ ██║");
        _writer.WriteLine("  ██║     ██████╔╝██║╚██████╔╝");
        _writer.WriteLine("  ╚═╝     ╚═════╝ ╚═╝ ╚══▀▀═╝");
        _writer.WriteLine();
        _writer.WriteLine("  PBIQ  /  POWER BI QUALITY GATE");
        _writer.WriteLine("  SEMANTIC MODEL AUDIT / RULE PACK 01  /  LOCAL  /  READ-ONLY");
        _writer.WriteLine();
    }

    private void WriteSection(string number, string title)
    {
        _writer.WriteLine($"  {number}  {title}");
        _writer.WriteLine("  --------------------------------------------");
    }

    private void WriteGuidance(string why, string fix)
    {
        _writer.WriteLine("     WHY IT MATTERS");
        _writer.WriteLine($"     {why}");
        _writer.WriteLine();
        _writer.WriteLine("     HOW TO FIX");
        _writer.WriteLine($"     {fix}");
        _writer.WriteLine();
    }

    private static string Count(int amount, string singular, string plural) =>
        $"{amount} {(amount == 1 ? singular : plural)}";
}

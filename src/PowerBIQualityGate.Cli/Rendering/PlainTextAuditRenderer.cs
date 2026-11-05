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

    public void Render(string pbipPath, TomModelSnapshot snapshot, ModelQualityAuditResult result, int exitCode)
    {
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
        _writer.WriteLine("  Default policy: warnings and information do not fail the audit.");
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
        var verdict = result.Verdict switch
        {
            QualityAuditVerdict.Failed => "AUDIT: FAIL",
            QualityAuditVerdict.Passed when result.Findings.Count > 0 => "AUDIT: PASS WITH ADVISORIES",
            QualityAuditVerdict.Passed => "AUDIT: PASS",
            _ => "AUDIT: NOT EVALUATED"
        };

        _writer.WriteLine($"  {verdict}");
        _writer.WriteLine($"  {Count(result.ErrorCount, "error", "errors")}  |  {Count(result.WarningCount, "warning", "warnings")}  |  {result.InformationCount} information");
        _writer.WriteLine($"  {result.RulesEvaluated} checks evaluated  |  exit code: {exitCode}");
        _writer.WriteLine("  Scope: metadata-only advisory rules; not a full model or DAX validation.");
        _writer.WriteLine("  Warnings and information do not block the default quality gate.");
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

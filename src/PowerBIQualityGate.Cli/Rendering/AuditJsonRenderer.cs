using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

using PowerBIQualityGate.Core.Inspection;
using PowerBIQualityGate.Core.Quality;
using PowerBIQualityGate.Core.Validation;

namespace PowerBIQualityGate.Cli.Rendering;

/// <summary>
/// Schema-v1 machine output for audit. Reads the already-evaluated quality
/// result; it never reruns quality rules or invents TMDL coordinates.
/// </summary>
public static class AuditJsonRenderer
{
    public const string SchemaVersion = "1.0.0";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public static void RenderSuccess(
        TextWriter writer,
        string pbipPath,
        TomModelSnapshot snapshot,
        ModelQualityAuditResult audit,
        QualityGateDecision decision,
        ModelAuditCoverage coverage, string? definitionDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(pbipPath);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(coverage);

        var measures = coverage.MeasureFormatting;
        var inspection = coverage.Sm002Inspection;
        var assessment = coverage.Sm002Assessment;
        var candidateCount = decision.CandidateMeasures;
        var resolver = definitionDirectory is null ? null :
            new TmdlSourceLocationResolver(definitionDirectory,
                Path.GetDirectoryName(Path.GetFullPath(pbipPath))!);

        AuditLocationV1 Location(ModelQualityFinding finding)
        {
            var coordinate = resolver?.Resolve(finding.RuleCode, finding.Location, snapshot);
            return new AuditLocationV1(finding.Location, coordinate?.FilePath, coordinate?.Line);
        }

        var report = new AuditReportV1(
            SchemaVersion,
            new AuditToolV1("pbiq", Version()),
            "audit",
            new AuditProjectV1(Path.GetFileName(pbipPath), snapshot.Name,
                snapshot.Tables.Count, snapshot.Measures, snapshot.Relationships.Count),
            new AuditResultV1(Status(decision.Outcome), decision.ExitCode),
            new AuditSummaryV1(audit.RulesEvaluated, audit.ErrorCount,
                audit.WarningCount, audit.InformationCount),
            new AuditPolicyV1(decision.FailOnWarning, decision.MinimumMeasureCoverage),
            measures is null ? null : new AuditCoverageV1(new AuditMeasureCoverageV1(
                inspection?.Inspected ?? 0,
                measures.Visible,
                measures.CheckedNumeric,
                candidateCount,
                measures.UnknownType,
                assessment?.NeedsReview ?? 0,
                candidateCount > 0
                    ? Math.Round(100m * decision.CheckedMeasures / candidateCount, 2)
                    : null,
                decision.HasMeasureCoverageGaps)),
            audit.EvaluatedRuleCodes.ToArray(),
            audit.Findings.Select(finding => new AuditDiagnosticV1(
                finding.RuleCode,
                finding.RuleTitle,
                Severity(finding.Severity),
                finding.Message,
                Location(finding),
                new AuditRemediationV1(finding.WhyItMatters, finding.HowToFix))).ToArray(),
            null);

        Write(writer, report);
    }

    public static void RenderFailure(
        TextWriter writer,
        string? inputPath,
        string code,
        string message,
        int exitCode,
        bool failOnWarning = false,
        int? minimumMeasureCoverage = null)
    {
        ArgumentNullException.ThrowIfNull(writer);
        var safeMessage = code switch
        {
            "TOM DESERIALIZATION FAILED" => "The local TMDL model could not be deserialized.",
            "QUALITY RULE EXECUTION FAILED" => "A semantic-model rule could not be evaluated.",
            "MEASURE QUALITY INTEGRITY FAILED" => "The SM-002 aggregate checks were inconsistent.",
            _ => message
        };
        var filename = string.IsNullOrWhiteSpace(inputPath)
            ? null
            : Path.GetFileName(inputPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var report = new AuditReportV1(
            SchemaVersion,
            new AuditToolV1("pbiq", Version()),
            "audit",
            new AuditProjectV1(filename, null, null, null, null),
            new AuditResultV1("not_evaluated", exitCode),
            new AuditSummaryV1(0, 0, 0, 0),
            new AuditPolicyV1(failOnWarning, minimumMeasureCoverage),
            null,
            [],
            [],
            new AuditFailureV1(code.Replace(' ', '_'), safeMessage));
        Write(writer, report);
    }

    private static void Write(TextWriter writer, AuditReportV1 report) =>
        writer.WriteLine(JsonSerializer.Serialize(report, Options));

    private static string Version()
    {
        // AssemblyVersion drops SemVer pre-release labels. InformationalVersion
        // preserves them, and may also contain CI build metadata (+SHA).
        var value = typeof(AuditJsonRenderer).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        return string.IsNullOrWhiteSpace(value) ? "dev" : value;
    }

    private static string Severity(DiagnosticSeverity severity) => severity switch
    {
        DiagnosticSeverity.Error => "error",
        DiagnosticSeverity.Warning => "warning",
        _ => "information"
    };

    private static string Status(QualityGateOutcome outcome) => outcome switch
    {
        QualityGateOutcome.Passed => "pass",
        QualityGateOutcome.PassedWithAdvisories => "pass_with_advisories",
        QualityGateOutcome.PassedWithCoverageGaps => "pass_with_coverage_gaps",
        QualityGateOutcome.Failed => "fail",
        QualityGateOutcome.FailedOnWarnings => "fail_warning_policy",
        QualityGateOutcome.FailedOnCoverage => "fail_coverage_policy",
        _ => "not_evaluated"
    };
}

/// <summary>
/// Explicit schema-v1 records prevent accidentally serializing internal TOM
/// snapshots, DAX expressions, or future engine properties into public output.
/// Additive fields require a schema update; breaking changes require v2.
/// </summary>
public sealed record AuditReportV1(
    string SchemaVersion,
    AuditToolV1 Tool,
    string Command,
    AuditProjectV1 Project,
    AuditResultV1 Result,
    AuditSummaryV1 Summary,
    AuditPolicyV1 Policy,
    AuditCoverageV1? Coverage,
    IReadOnlyList<string> Rules,
    IReadOnlyList<AuditDiagnosticV1> Diagnostics,
    AuditFailureV1? Failure);

public sealed record AuditToolV1(string Name, string Version);
public sealed record AuditProjectV1(string? Name, string? ModelName,
    int? Tables, int? Measures, int? Relationships);
public sealed record AuditResultV1(string Status, int ExitCode);
public sealed record AuditSummaryV1(int RulesEvaluated, int Errors, int Warnings, int Information);
public sealed record AuditPolicyV1(bool FailOnWarning, int? MinimumMeasureCoverage);
public sealed record AuditCoverageV1(AuditMeasureCoverageV1 Measures);
public sealed record AuditMeasureCoverageV1(
    int Inspected, int Visible, int CheckedNumeric, int Candidates,
    int UnknownType, int NeedsReview, decimal? ClassificationPercent, bool HasGaps);
public sealed record AuditDiagnosticV1(string RuleId, string Title, string Severity,
    string Message, AuditLocationV1 Location, AuditRemediationV1 Remediation);
public sealed record AuditLocationV1(string ObjectPath, string? FilePath, int? Line);
public sealed record AuditRemediationV1(string Why, string How);
public sealed record AuditFailureV1(string Code, string Message);

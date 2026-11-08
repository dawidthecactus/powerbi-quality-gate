using PowerBIQualityGate.Core.Inspection;

namespace PowerBIQualityGate.Cli.Rendering;

/// <summary>Aggregate-only diagnostic output. Never prints model object names.</summary>
public sealed class DaxDependencySummaryRenderer
{
    private readonly TextWriter _writer;

    public DaxDependencySummaryRenderer(TextWriter writer) =>
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));

    public void Render(DaxDependencyReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        _writer.WriteLine("DAX dependency discovery (advisory; not validated DAX errors):");
        _writer.WriteLine($"  Measures inspected: {report.InspectedMeasures}");
        _writer.WriteLine($"  Expressions skipped (unsupported lexical shape/limit): {report.SkippedExpressions}");
        _writer.WriteLine($"  Measures with candidate references: {report.AffectedMeasures}");
        _writer.WriteLine($"  Candidate references: {report.Candidates.Count}");
        _writer.WriteLine($"    Unknown table qualifiers: {report.Count(DaxDependencyCandidateKind.MissingTable)}");
        _writer.WriteLine($"    Unknown member in known table: {report.Count(DaxDependencyCandidateKind.MissingQualifiedMember)}");
        _writer.WriteLine($"    Unresolved unqualified [Name]: {report.Count(DaxDependencyCandidateKind.UnresolvedUnqualifiedReference)}");
        _writer.WriteLine("  No DAX execution. Virtual columns and context-sensitive references may cause false positives.");
        _writer.WriteLine("  No change to SM-002 warnings, coverage, audit results or exit codes.");
    }
}

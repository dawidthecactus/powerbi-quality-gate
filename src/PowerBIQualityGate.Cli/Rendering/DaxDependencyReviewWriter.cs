using System.Text;
using System.Text.RegularExpressions;

using PowerBIQualityGate.Core.Inspection;

namespace PowerBIQualityGate.Cli.Rendering;

/// <summary>
/// Private, opt-in candidate review CSV. It contains object identifiers and
/// therefore MUST remain outside Git and must NEVER be shared as sanitized.
/// </summary>
public static class DaxDependencyReviewWriter
{
    private const string Header =
        "case_id,measure_table,measure_name,reference_table,reference_name,candidate_kind,review_status,notes";

    public static int Write(string path, string caseId, DaxDependencyReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(caseId) ||
            !Regex.IsMatch(caseId, @"^P0[1-9]$", RegexOptions.CultureInvariant))
            throw new ArgumentException("Invalid private dependency review configuration.");

        var destination = Path.GetFullPath(path);
        VerifyPrivateDestination(destination);
        using var stream = new FileStream(destination, FileMode.CreateNew,
            FileAccess.Write, FileShare.None);
        try
        {
            using var writer = new StreamWriter(stream, new UTF8Encoding(false),
                bufferSize: 1024, leaveOpen: true);
            writer.WriteLine(Header);
            foreach (var candidate in report.Candidates)
            {
                writer.WriteLine(string.Join(',',
                    Csv(caseId), Csv(candidate.MeasureTable),
                    Csv(candidate.MeasureName), Csv(candidate.ReferenceTable ?? string.Empty),
                    Csv(candidate.ReferenceName), Csv(CandidateLabel(candidate.Kind)),
                    Csv(string.Empty), Csv(string.Empty)));
            }
            writer.Flush();
        }
        catch
        {
            stream.Dispose();
            try { File.Delete(destination); } catch (IOException) { }
            throw;
        }
        return report.Candidates.Count;
    }

    private static string CandidateLabel(DaxDependencyCandidateKind kind) => kind switch
    {
        DaxDependencyCandidateKind.MissingTable => "unknown_table",
        DaxDependencyCandidateKind.MissingQualifiedMember => "unknown_qualified_member",
        DaxDependencyCandidateKind.UnresolvedUnqualifiedReference => "unresolved_unqualified",
        _ => "unknown"
    };

    private static string Csv(string value)
    {
        value = value.Replace("\0", string.Empty, StringComparison.Ordinal);
        var trimmed = value.TrimStart();
        if (trimmed.Length > 0 && trimmed[0] is '=' or '+' or '-' or '@')
            value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    private static void VerifyPrivateDestination(string path)
    {
        if (!string.Equals(Path.GetExtension(path), ".csv", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Private review must use a CSV file.");
        if (File.Exists(path) || Directory.Exists(path))
            throw new IOException("Private dependency review already exists.");
        var parent = Directory.GetParent(path);
        if (parent is null || !parent.Exists)
            throw new DirectoryNotFoundException("Private output directory must already exist.");
        for (var current = parent; current is not null; current = current.Parent)
        {
            if (Directory.Exists(Path.Combine(current.FullName, ".git")) ||
                File.Exists(Path.Combine(current.FullName, ".git")))
                throw new ArgumentException("Private dependency review must be outside Git.");
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new ArgumentException("Private dependency review may not use linked directories.");
        }
    }
}

using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

using PowerBIQualityGate.Core.Inspection;

namespace PowerBIQualityGate.Cli.Rendering;

/// <summary>
/// Opt-in, PRIVATE reviewer mapping for advisory type evidence. This CSV contains
/// table/measure names, so it must never be committed or treated as sanitized output.
/// Does not export any DAX expressions or dynamic format strings.
/// </summary>
public static class EvidenceValidationSampleWriter
{
    private const int DefaultPerStratum = 12;
    private const int MaximumPerStratum = 100;
    private const string Header =
        "case_id,sample_id,table_name,measure_name,evidence_source,predicted_kind,observed_kind,notes";

    private sealed record Candidate(
        string TableName, string MeasureName, string Source, string Predicted,
        string SelectionHash);

    /// <summary>
    /// Samples at most perStratum items for each of static/var x numeric/nonnumeric.
    /// Stable by case alias and object names for a fixed model, not random or
    /// statistically representative. Output path MUST be outside every Git tree.
    /// </summary>
    public static int Write(string path, string caseId, MeasureInspectionReport report,
        int perStratum = DefaultPerStratum)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (string.IsNullOrWhiteSpace(path) ||
            string.IsNullOrWhiteSpace(caseId) ||
            !Regex.IsMatch(caseId, @"^P0[1-9]$", RegexOptions.CultureInvariant) ||
            perStratum is < 1 or > MaximumPerStratum)
        {
            throw new ArgumentException("Invalid private sampling configuration.");
        }

        var destination = Path.GetFullPath(path);
        VerifyPrivateDestination(destination);

        var candidates = report.Measures
            .Where(measure => measure.IsVisible &&
                (measure.DataTypeKind is MeasureDataTypeKind.Unknown or MeasureDataTypeKind.Unsupported))
            .Select(measure =>
            {
                var kind = measure.StaticDaxTypeEvidence;
                var source = "static";
                if (kind is not (StaticDaxTypeEvidenceKind.LikelyNumeric or
                    StaticDaxTypeEvidenceKind.LikelyNonNumeric))
                {
                    kind = measure.VarReturnEvidence;
                    source = "var";
                }
                if (kind is not (StaticDaxTypeEvidenceKind.LikelyNumeric or
                    StaticDaxTypeEvidenceKind.LikelyNonNumeric))
                {
                    return null;
                }

                var predicted = kind == StaticDaxTypeEvidenceKind.LikelyNumeric
                    ? "numeric" : "nonnumeric";
                var material = string.Join('\u001f', "sm002.4/v1", caseId, source,
                    predicted, measure.TableName, measure.MeasureName);
                var hash = Convert.ToHexString(
                    SHA256.HashData(Encoding.UTF8.GetBytes(material)));
                return new Candidate(measure.TableName, measure.MeasureName,
                    source, predicted, hash);
            })
            .OfType<Candidate>()
            .GroupBy(item => (item.Source, item.Predicted))
            .OrderBy(group => group.Key.Source, StringComparer.Ordinal)
            .ThenBy(group => group.Key.Predicted, StringComparer.Ordinal)
            .SelectMany(group => group.OrderBy(item => item.SelectionHash,
                StringComparer.Ordinal).Take(perStratum))
            .ToArray();

        // FileMode.CreateNew prevents overwriting previous private reviews.
        // Create the file only after every validation and selection step passes.
        using var stream = new FileStream(destination, FileMode.CreateNew,
            FileAccess.Write, FileShare.None);
        try
        {
            using var writer = new StreamWriter(stream, new UTF8Encoding(false),
                bufferSize: 1024, leaveOpen: true);
            writer.WriteLine(Header);
            for (var index = 0; index < candidates.Length; index++)
            {
                var item = candidates[index];
                writer.WriteLine(string.Join(',',
                    Csv(caseId), Csv($"S{index + 1:000}"),
                    Csv(item.TableName), Csv(item.MeasureName),
                    Csv(item.Source), Csv(item.Predicted), Csv(string.Empty),
                    Csv(string.Empty)));
            }
            writer.Flush();
        }
        catch
        {
            stream.Dispose();
            try { File.Delete(destination); } catch (IOException) { }
            throw;
        }
        return candidates.Length;
    }

    private static string Csv(string value)
    {
        value = value.Replace("\0", string.Empty, StringComparison.Ordinal);
        // Prevent spreadsheet formula evaluation on a private review CSV.
        var first = value.TrimStart();
        if (first.Length > 0 && first[0] is '=' or '+' or '-' or '@')
            value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    private static void VerifyPrivateDestination(string path)
    {
        if (!string.Equals(Path.GetExtension(path), ".csv",
                StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Private sample must be a CSV file.");
        if (File.Exists(path) || Directory.Exists(path))
            throw new IOException("Private review file already exists.");

        var parent = Directory.GetParent(path);
        if (parent is null || !parent.Exists)
            throw new DirectoryNotFoundException("Private output directory must exist.");
        for (var current = parent; current is not null; current = current.Parent)
        {
            if (Directory.Exists(Path.Combine(current.FullName, ".git")) ||
                File.Exists(Path.Combine(current.FullName, ".git")))
                throw new ArgumentException("Private review must be outside Git.");
            // Reject symlink/junction parents; they can bypass lexical Git checks.
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new ArgumentException("Private review may not use linked directories.");
        }
    }
}

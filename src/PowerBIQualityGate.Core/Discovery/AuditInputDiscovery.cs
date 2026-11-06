namespace PowerBIQualityGate.Core.Discovery;

/// <summary>Which source format supplied the semantic model for an audit.</summary>
public enum AuditInputKind
{
    Pbip,
    FabricSemanticModel
}

/// <summary>
/// EntryPath is either a real .pbip shortcut or a real .SemanticModel folder.
/// No temporary or synthetic PBIP is ever created.
/// </summary>
public sealed record AuditInputDiscoveryResult(
    DiscoveryStatus Status,
    AuditInputKind Kind,
    string? EntryPath,
    string Message)
{
    public bool IsSuccess => Status == DiscoveryStatus.Found;
}

/// <summary>
/// Preserves existing PBIP priority and discovers one local Fabric Git
/// SemanticModel item when there is no PBIP shortcut. Intentionally does
/// not recurse through arbitrary directories or choose one of many models.
/// </summary>
public sealed class AuditInputDiscovery
{
    public AuditInputDiscoveryResult Discover(string inputPath)
    {
        // Do not silently reinterpret invalid or ambiguous PBIP projects.
        var pbip = new PbipProjectDiscovery().Discover(inputPath);
        if (pbip.IsSuccess)
        {
            return new AuditInputDiscoveryResult(DiscoveryStatus.Found,
                AuditInputKind.Pbip, pbip.PbipPath, pbip.Message);
        }
        if (pbip.Status != DiscoveryStatus.NotFound || string.IsNullOrWhiteSpace(inputPath))
        {
            return Failed(pbip.Status, pbip.Message);
        }

        string directory;
        try
        {
            directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(inputPath));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Failed(DiscoveryStatus.InvalidPath, "Invalid project path.");
        }
        if (!Directory.Exists(directory))
        {
            return Failed(pbip.Status, pbip.Message);
        }

        if (IsSemanticModelDirectory(directory))
        {
            return Found(directory);
        }

        try
        {
            // Fabric Git stores one item per <displayName>.SemanticModel folder.
            var models = Directory.EnumerateDirectories(directory, "*", SearchOption.TopDirectoryOnly)
                .Where(IsSemanticModelDirectory)
                .OrderBy(path => path, StringComparer.Ordinal)
                .Take(2)
                .ToArray();
            return models.Length switch
            {
                1 => Found(models[0]),
                2 => Failed(DiscoveryStatus.MultipleProjects,
                    "Multiple Fabric .SemanticModel folders found. Specify one explicitly."),
                _ => Failed(pbip.Status, pbip.Message)
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Failed(DiscoveryStatus.AccessDenied, "Project directory could not be read.");
        }
    }

    private static bool IsSemanticModelDirectory(string path) =>
        Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            .EndsWith(".SemanticModel", StringComparison.OrdinalIgnoreCase);

    private static AuditInputDiscoveryResult Found(string directory) =>
        new(DiscoveryStatus.Found, AuditInputKind.FabricSemanticModel,
            directory, "Fabric semantic model directory discovered.");

    private static AuditInputDiscoveryResult Failed(DiscoveryStatus status, string message) =>
        new(status, AuditInputKind.Pbip, null, message);
}

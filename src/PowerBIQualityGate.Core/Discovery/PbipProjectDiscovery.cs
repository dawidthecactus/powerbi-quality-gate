namespace PowerBIQualityGate.Core.Discovery;

public sealed class PbipProjectDiscovery
{
    public ProjectDiscoveryResult Discover(string inputPath)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
        {
            return new(
                DiscoveryStatus.InvalidPath,
                null,
                "Project path cannot be empty.");
        }

        string path;

        try
        {
            path = Path.GetFullPath(inputPath);
        }
        catch (Exception ex) when (
            ex is ArgumentException or
            NotSupportedException or
            PathTooLongException)
        {
            return new(
                DiscoveryStatus.InvalidPath,
                null,
                "Invalid project path.");
        }

        if (File.Exists(path))
        {
            if (!IsPbip(path))
            {
                return new(
                    DiscoveryStatus.InvalidPath,
                    null,
                    "Expected a .pbip file.");
            }

            return Found(path);
        }

        if (!Directory.Exists(path))
        {
            return new(
                DiscoveryStatus.NotFound,
                null,
                "Project path does not exist.");
        }

        try
        {
            var files = Directory
                .EnumerateFiles(
                    path,
                    "*",
                    SearchOption.TopDirectoryOnly)
                .Where(IsPbip)
                .OrderBy(
                    file => file,
                    StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return files.Length switch
            {
                0 => new(
                    DiscoveryStatus.NotFound,
                    null,
                    "No .pbip file found."),

                1 => Found(files[0]),

                _ => new(
                    DiscoveryStatus.MultipleProjects,
                    null,
                    "Multiple .pbip files found. Specify one explicitly.")
            };
        }
        catch (UnauthorizedAccessException)
        {
            return new(
                DiscoveryStatus.AccessDenied,
                null,
                "Access to project directory was denied.");
        }
        catch (IOException)
        {
            return new(
                DiscoveryStatus.AccessDenied,
                null,
                "Project directory could not be read.");
        }
    }

    private static bool IsPbip(string path) =>
        string.Equals(
            Path.GetExtension(path),
            ".pbip",
            StringComparison.OrdinalIgnoreCase);

    private static ProjectDiscoveryResult Found(string path) =>
        new(
            DiscoveryStatus.Found,
            path,
            "PBIP entry point discovered.");
}

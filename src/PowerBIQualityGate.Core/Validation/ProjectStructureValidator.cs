using System.Text.Json;

namespace PowerBIQualityGate.Core.Validation;

/// <summary>
/// Read-only structural validation of a PBIP entry point and its report/model metadata.
/// Does not claim to perform complete JSON Schema, PBIR or TMDL validation.
/// </summary>
public sealed class ProjectStructureValidator
{
    public ProjectStructureValidationResult Validate(string pbipPath)
    {
        var state = new ValidationState();

        if (string.IsNullOrWhiteSpace(pbipPath))
        {
            state.Error("PBI-001", "PBIP path cannot be empty.", pbipPath ?? string.Empty);
            return state.Result();
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(pbipPath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            state.Error("PBI-001", "Invalid PBIP path.", pbipPath);
            return state.Result();
        }

        if (!TryReadJson(fullPath, "PBI-001", state, out var shortcut))
        {
            return state.Result();
        }

        var projectRoot = Path.GetDirectoryName(fullPath)!;
        string? reportRelativePath;

        using (shortcut)
        {
            var root = shortcut!.RootElement;
            if (ReadString(root, "version") != "1.0")
            {
                state.Error("PBI-002", "Expected PBIP shortcut version '1.0'.", fullPath);
                return state.Result();
            }

            if (!root.TryGetProperty("artifacts", out var artifacts) ||
                artifacts.ValueKind != JsonValueKind.Array ||
                artifacts.GetArrayLength() != 1)
            {
                state.Error("PBI-003", "Expected exactly one report artifact in PBIP.", fullPath);
                return state.Result();
            }

            reportRelativePath = ReadString(artifacts[0], "report", "path");
            if (string.IsNullOrWhiteSpace(reportRelativePath))
            {
                state.Error("PBI-003", "Missing artifacts[0].report.path.", fullPath);
                return state.Result();
            }
        }

        if (!TryResolveInside(projectRoot, reportRelativePath!, projectRoot, out var reportDirectory))
        {
            state.Error("PBI-004", "Report path must be relative and stay inside the PBIP directory.", fullPath);
            return state.Result();
        }

        state.ReportDirectory = reportDirectory;
        if (!Directory.Exists(reportDirectory))
        {
            state.Error("PBI-005", "Referenced report directory does not exist.", reportDirectory);
            return state.Result();
        }

        ValidateReport(reportDirectory, projectRoot, state);
        return state.Result();
    }

    private static void ValidateReport(string reportDirectory, string projectRoot, ValidationState state)
    {
        var manifestPath = Path.Combine(reportDirectory, "definition.pbir");
        if (!TryReadJson(manifestPath, "PBI-006", state, out var reportManifest))
        {
            return;
        }

        using (reportManifest)
        {
            var root = reportManifest!.RootElement;
            var version = ReadVersion(root);
            if (version is null || !IsSupportedVersion(version))
            {
                state.Error("PBI-007", "Unsupported or missing report definition version (expected 1.0 or 4.0+).", manifestPath);
                return;
            }

            ValidateReportPayload(reportDirectory, version, state);
            ValidateDatasetReference(root, reportDirectory, projectRoot, state, manifestPath);
        }
    }

    private static void ValidateReportPayload(string reportDirectory, Version version, ValidationState state)
    {
        var definitionDirectory = Path.Combine(reportDirectory, "definition");
        var legacyReportPath = Path.Combine(reportDirectory, "report.json");
        var hasPbir = Directory.Exists(definitionDirectory);
        var hasLegacy = File.Exists(legacyReportPath);

        if (hasPbir == hasLegacy)
        {
            state.Error("PBI-008", "Expected exactly one report format: definition/ (PBIR) or report.json (legacy).", reportDirectory);
            return;
        }

        if (hasLegacy)
        {
            state.ReportFormat = ReportDefinitionFormat.PbirLegacy;
            ValidateJsonOnly(legacyReportPath, "PBI-010", state);
            return;
        }

        state.ReportFormat = ReportDefinitionFormat.Pbir;
        if (version.Major < 4)
        {
            state.Error("PBI-007", "PBIR folder requires report definition version 4.0 or newer.", reportDirectory);
        }

        foreach (var requiredFile in new[] { "report.json", "version.json" })
        {
            ValidateJsonOnly(Path.Combine(definitionDirectory, requiredFile), "PBI-009", state);
        }

        var pages = Path.Combine(definitionDirectory, "pages");
        if (!Directory.Exists(pages))
        {
            state.Error("PBI-009", "Required PBIR pages/ directory is missing.", pages);
            return;
        }

        // Only page and visual structure is checked here, not complete PBIR schema compliance.
        try
        {
            foreach (var page in Directory.EnumerateDirectories(pages))
            {
                ValidateJsonOnly(Path.Combine(page, "page.json"), "PBI-009", state);
                var visuals = Path.Combine(page, "visuals");
                if (!Directory.Exists(visuals))
                {
                    continue;
                }

                foreach (var visual in Directory.EnumerateDirectories(visuals))
                {
                    ValidateJsonOnly(Path.Combine(visual, "visual.json"), "PBI-009", state);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            state.Error("PBI-009", "Unable to enumerate PBIR pages or visuals.", pages);
        }
    }

    private static void ValidateDatasetReference(
        JsonElement reportManifest,
        string reportDirectory,
        string projectRoot,
        ValidationState state,
        string manifestPath)
    {
        if (!reportManifest.TryGetProperty("datasetReference", out var reference) ||
            reference.ValueKind != JsonValueKind.Object)
        {
            state.Error("PBI-011", "Missing datasetReference in definition.pbir.", manifestPath);
            return;
        }

        var hasPath = HasObject(reference, "byPath");
        var hasConnection = HasObject(reference, "byConnection");

        if (hasPath == hasConnection)
        {
            state.Error("PBI-011", "Specify exactly one dataset reference: byPath or byConnection.", manifestPath);
            return;
        }

        if (hasConnection)
        {
            if (string.IsNullOrWhiteSpace(ReadString(reference, "byConnection", "connectionString")))
            {
                state.Error("PBI-011", "byConnection requires a nonempty connectionString.", manifestPath);
                return;
            }

            state.SemanticModelFormat = SemanticModelDefinitionFormat.External;
            return;
        }

        var modelRelativePath = ReadString(reference, "byPath", "path");
        if (string.IsNullOrWhiteSpace(modelRelativePath) ||
            !TryResolveInside(reportDirectory, modelRelativePath, projectRoot, out var modelDirectory))
        {
            state.Error("PBI-012", "byPath must be relative and remain within the PBIP directory.", manifestPath);
            return;
        }

        state.SemanticModelDirectory = modelDirectory;
        if (!Directory.Exists(modelDirectory))
        {
            state.Error("PBI-013", "Referenced semantic model directory does not exist.", modelDirectory);
            return;
        }

        ValidateSemanticModel(modelDirectory, state);
    }

    private static void ValidateSemanticModel(string modelDirectory, ValidationState state)
    {
        var manifestPath = Path.Combine(modelDirectory, "definition.pbism");
        if (!TryReadJson(manifestPath, "PBI-014", state, out var modelManifest))
        {
            return;
        }

        Version? version;
        using (modelManifest)
        {
            version = ReadVersion(modelManifest!.RootElement);
        }

        if (version is null || !IsSupportedVersion(version))
        {
            state.Error("PBI-015", "Unsupported or missing semantic model definition version (expected 1.0 or 4.0+).", manifestPath);
            return;
        }

        var tmdlDirectory = Path.Combine(modelDirectory, "definition");
        var bimPath = Path.Combine(modelDirectory, "model.bim");
        var hasTmdl = Directory.Exists(tmdlDirectory);
        var hasTmsl = File.Exists(bimPath);

        if (hasTmdl == hasTmsl)
        {
            state.Error("PBI-016", "Expected exactly one model format: definition/ (TMDL) or model.bim (TMSL).", modelDirectory);
            return;
        }

        if (hasTmsl)
        {
            state.SemanticModelFormat = SemanticModelDefinitionFormat.Tmsl;
            ValidateJsonOnly(bimPath, "PBI-018", state);
            return;
        }

        state.SemanticModelFormat = SemanticModelDefinitionFormat.Tmdl;
        if (version.Major < 4)
        {
            state.Error("PBI-015", "TMDL folder requires semantic model definition version 4.0 or newer.", manifestPath);
        }

        foreach (var requiredFile in new[] { "database.tmdl", "model.tmdl" })
        {
            var path = Path.Combine(tmdlDirectory, requiredFile);
            if (!File.Exists(path))
            {
                state.Error("PBI-017", $"Required TMDL file '{requiredFile}' is missing.", path);
            }
        }
    }

    private static bool HasObject(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.Object;

    private static string? ReadString(JsonElement parent, params string[] path)
    {
        var current = parent;
        foreach (var segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object ||
                !current.TryGetProperty(segment, out var next))
            {
                return null;
            }

            current = next;
        }

        return current.ValueKind == JsonValueKind.String ? current.GetString() : null;
    }

    private static Version? ReadVersion(JsonElement root)
    {
        var value = ReadString(root, "version");
        return Version.TryParse(value, out var version) ? version : null;
    }

    private static bool IsSupportedVersion(Version? version) =>
        version is not null &&
        ((version.Major == 1 && version.Minor == 0) || version.Major >= 4);

    private static bool TryResolveInside(string baseDirectory, string relative, string rootDirectory, out string result)
    {
        result = string.Empty;
        if (string.IsNullOrWhiteSpace(relative) || relative.Contains('\\') || Path.IsPathRooted(relative))
        {
            return false;
        }

        try
        {
            result = Path.GetFullPath(Path.Combine(
                baseDirectory,
                relative.Replace('/', Path.DirectorySeparatorChar)));

            var relToRoot = Path.GetRelativePath(rootDirectory, result);
            return relToRoot != ".." &&
                   !relToRoot.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                   !Path.IsPathRooted(relToRoot);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static void ValidateJsonOnly(string path, string code, ValidationState state)
    {
        if (TryReadJson(path, code, state, out var document))
        {
            document!.Dispose();
        }
    }

    private static bool TryReadJson(
        string path,
        string code,
        ValidationState state,
        out JsonDocument? document)
    {
        document = null;

        try
        {
            if (!File.Exists(path))
            {
                state.Error(code, "Required JSON file does not exist.", path);
                return false;
            }

            document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                document.Dispose();
                document = null;
                state.Error(code, "Expected a JSON object.", path);
                return false;
            }

            return true;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            state.Error(code, "JSON file is invalid or could not be read.", path);
            return false;
        }
    }

    private sealed class ValidationState
    {
        public string? ReportDirectory { get; set; }
        public ReportDefinitionFormat ReportFormat { get; set; }
        public string? SemanticModelDirectory { get; set; }
        public SemanticModelDefinitionFormat SemanticModelFormat { get; set; }
        private List<ProjectDiagnostic> Diagnostics { get; } = [];

        public void Error(string code, string message, string path) =>
            Diagnostics.Add(new ProjectDiagnostic(code, DiagnosticSeverity.Error, message, path));

        public ProjectStructureValidationResult Result() => new(
            ReportDirectory,
            ReportFormat,
            SemanticModelDirectory,
            SemanticModelFormat,
            Diagnostics.ToArray());
    }
}

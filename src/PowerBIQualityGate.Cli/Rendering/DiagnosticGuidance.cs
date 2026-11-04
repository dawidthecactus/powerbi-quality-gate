using PowerBIQualityGate.Core.Discovery;

namespace PowerBIQualityGate.Cli.Rendering;

/// <summary>
/// User-facing explanations for the structural checks currently implemented in Core.
/// These are suggestions, never automatic fixes. Unknown codes do not invent advice.
/// </summary>
public static class DiagnosticGuidance
{
    public sealed record Advice(string Area, string Why, string Fix);

    private static readonly IReadOnlyDictionary<string, Advice> Checks =
        new Dictionary<string, Advice>(StringComparer.Ordinal)
        {
            ["PBI-001"] = new("PBIP entry point",
                "The scanner needs a readable .pbip file containing a JSON object before it can locate the report.",
                "Check the file path and permissions. Restore or re-export the .pbip file if its JSON is damaged."),
            ["PBI-002"] = new("PBIP version",
                "The PBIP shortcut version identifies the project structure the scanner can interpret reliably.",
                "Verify that the .pbip file contains \"version\": \"1.0\". Prefer re-saving the project in Power BI Desktop over editing an unfamiliar manifest."),
            ["PBI-003"] = new("Report reference",
                "The current scanner supports exactly one report artifact with a usable report.path in the .pbip shortcut.",
                "Inspect artifacts in the .pbip file. Ensure there is one report entry with report.path, or scan a supported single-report PBIP export."),
            ["PBI-004"] = new("Report path safety",
                "Report references must be relative and stay inside the PBIP root; escaping the root can point to unrelated files.",
                "Use a relative artifacts[0].report.path that points to a report folder inside the project directory."),
            ["PBI-005"] = new("Report folder",
                "The .pbip shortcut points to a folder that cannot be found, so the report definition cannot be inspected.",
                "Restore the referenced report folder or correct its relative path in the .pbip shortcut."),
            ["PBI-006"] = new("Report manifest",
                "The report's definition.pbir describes its version and semantic-model reference; missing or invalid JSON blocks inspection.",
                "Restore definition.pbir from a valid PBIP export and make sure it contains a JSON object."),
            ["PBI-007"] = new("Report definition version",
                "The report manifest version must be supported, and an enhanced PBIR folder requires version 4.0 or newer.",
                "Make the report manifest and on-disk report format consistent. Prefer a fresh project export from Power BI Desktop."),
            ["PBI-008"] = new("Report payload",
                "The scanner needs exactly one report representation: enhanced definition/ or legacy report.json.",
                "Keep the representation used by the report manifest, and remove conflicting or restore missing report payloads."),
            ["PBI-009"] = new("Enhanced PBIR files",
                "Enhanced PBIR relies on required JSON files and page/visual folders. A missing or malformed file prevents structural inspection.",
                "Check the reported path; restore the required JSON file or directory from a complete report export."),
            ["PBI-010"] = new("Legacy report JSON",
                "The legacy report.json must exist and contain a readable JSON object to validate the report layout.",
                "Restore report.json from a valid legacy report export and confirm that it is valid JSON."),
            ["PBI-011"] = new("Semantic model reference",
                "The report must reference either a local model (byPath) or an external model (byConnection), not both or neither.",
                "Inspect datasetReference in definition.pbir. Set one valid reference; byConnection needs a nonempty connectionString."),
            ["PBI-012"] = new("Local model path",
                "A byPath model reference must be relative and remain inside the PBIP project root.",
                "Correct datasetReference.byPath.path to a relative path pointing to a model folder within the project root."),
            ["PBI-013"] = new("Semantic model folder",
                "The report references a local semantic model directory that is missing from the project.",
                "Restore the referenced semantic model folder or correct datasetReference.byPath.path."),
            ["PBI-014"] = new("Semantic model manifest",
                "The local model's definition.pbism identifies its format version; missing or malformed JSON blocks validation.",
                "Restore definition.pbism from the original model export and confirm that it is valid JSON."),
            ["PBI-015"] = new("Model definition version",
                "The model definition version must be supported, and TMDL requires version 4.0 or newer.",
                "Ensure definition.pbism and the model layout describe compatible versions. Prefer re-exporting the model."),
            ["PBI-016"] = new("Model payload",
                "A local semantic model must use exactly one representation: TMDL definition/ or TMSL model.bim.",
                "Restore the missing representation or remove a conflicting one after confirming which format the model uses."),
            ["PBI-017"] = new("TMDL root files",
                "The current structural checks require database.tmdl and model.tmdl at the root of the TMDL definition folder.",
                "Restore the missing .tmdl file named in the diagnostic, preferably from a full PBIP export."),
            ["PBI-018"] = new("TMSL model file",
                "A TMSL model must have a model.bim file containing a readable JSON object.",
                "Restore or re-export model.bim and ensure that its JSON is valid.")
        };

    private static readonly Advice Unknown = new(
        "Unknown diagnostic",
        "The scanner reported a diagnostic without an explanation in this CLI version.",
        "Review the diagnostic message and path. Consult the project documentation or open an issue with a minimal example.");

    public static Advice ForCode(string code) =>
        Checks.TryGetValue(code, out var advice) ? advice : Unknown;

    public static Advice ForDiscovery(DiscoveryStatus status) => status switch
    {
        DiscoveryStatus.InvalidPath => new("Input path",
            "The supplied input is not a supported .pbip file or readable project path.",
            "Pass a .pbip file or a directory containing exactly one .pbip file. Check spelling and extension."),
        DiscoveryStatus.NotFound => new("Project discovery",
            "The specified project or its .pbip entry point could not be found.",
            "Check the path. If passing a directory, put exactly one .pbip file directly inside it."),
        DiscoveryStatus.MultipleProjects => new("Ambiguous project",
            "The directory contains multiple .pbip files; selecting one automatically could scan the wrong project.",
            "Provide the full path to the specific .pbip file you want to scan."),
        DiscoveryStatus.AccessDenied => new("File access",
            "The scanner could not enumerate the project directory with the current permissions.",
            "Check read permissions and whether another process is blocking access to the directory."),
        _ => new("Project discovery",
            "The scanner could not resolve a valid .pbip entry point.",
            "Check the supplied project path and try a direct .pbip file path.")
    };
}

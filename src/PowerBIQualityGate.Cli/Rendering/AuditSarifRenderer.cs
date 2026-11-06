using System.Reflection;
using System.Text.Json;

using PowerBIQualityGate.Core.Inspection;
using PowerBIQualityGate.Core.Quality;
using PowerBIQualityGate.Core.Validation;

namespace PowerBIQualityGate.Cli.Rendering;

/// <summary>
/// SARIF 2.1.0 export of evaluated, configured findings. Uses the existing
/// verified TMDL declaration resolver and never invents file coordinates.
/// </summary>
public static class AuditSarifRenderer
{
    private const string Schema = "https://json.schemastore.org/sarif-2.1.0.json";
    private const string InformationUri = "https://github.com/dawidthecactus/powerbi-quality-gate";

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static void RenderSuccess(
        TextWriter writer, string pbipPath, TomModelSnapshot snapshot,
        ModelQualityAuditResult audit, QualityGateDecision decision,
        string definitionDirectory, string? sourceRoot = null)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(pbipPath);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(definitionDirectory);

        var projectDirectory = Path.GetDirectoryName(Path.GetFullPath(pbipPath))!;
        var root = sourceRoot is null ? projectDirectory : Path.GetFullPath(sourceRoot);
        var resolver = new TmdlSourceLocationResolver(definitionDirectory, projectDirectory);
        var rules = ModelQualityRulePack.CreateDefault()
            .Where(rule => audit.EvaluatedRuleCodes.Contains(rule.Code, StringComparer.Ordinal))
            .Select(rule => (object)new
            {
                id = rule.Code,
                name = rule.Title,
                shortDescription = new { text = rule.Title }
            }).ToArray();

        var missingLocationCount = 0;
        var results = audit.Findings.Select(finding =>
        {
            var coordinate = resolver.Resolve(finding.RuleCode, finding.Location, snapshot);
            var uri = coordinate is null ? null : RepositoryUri(root, projectDirectory, coordinate.FilePath);
            object[] locations;
            if (uri is null || coordinate is null || coordinate.Line < 1)
            {
                missingLocationCount++;
                locations = [];
            }
            else
            {
                locations =
                [
                    new
                    {
                        physicalLocation = new
                        {
                            artifactLocation = new { uri },
                            region = new { startLine = coordinate.Line }
                        }
                    }
                ];
            }
            return (object)new
            {
                ruleId = finding.RuleCode,
                level = Level(finding.Severity),
                message = new { text = finding.Message },
                locations,
                properties = new
                {
                    objectPath = finding.Location,
                    why = finding.WhyItMatters,
                    howToFix = finding.HowToFix
                }
            };
        }).ToArray();

        Write(writer, new
        {
            version = "2.1.0",
            runs = new[]
            {
                new
                {
                    tool = new
                    {
                        driver = new
                        {
                            name = "pbiq",
                            informationUri = InformationUri,
                            semanticVersion = ToolVersion(),
                            rules
                        }
                    },
                    invocations = new[] { new { executionSuccessful = true } },
                    results,
                    properties = new
                    {
                        pbiqExitCode = decision.ExitCode,
                        pbiqOutcome = decision.Outcome.ToString(),
                        pbiqUnlocatedResults = missingLocationCount
                    }
                }
            }
        });
    }

    /// <summary>
    /// Emit a valid, empty SARIF run after a discovery/config/parser failure.
    /// The caller still returns its existing nonzero CLI exit code.
    /// </summary>
    public static void RenderFailure(TextWriter writer, string code, int exitCode)
    {
        ArgumentNullException.ThrowIfNull(writer);
        Write(writer, new
        {
            version = "2.1.0",
            runs = new[]
            {
                new
                {
                    tool = new
                    {
                        driver = new
                        {
                            name = "pbiq",
                            informationUri = InformationUri,
                            semanticVersion = ToolVersion(),
                            rules = Array.Empty<object>()
                        }
                    },
                    invocations = new[] { new { executionSuccessful = false } },
                    results = Array.Empty<object>(),
                    properties = new
                    {
                        pbiqExitCode = exitCode,
                        pbiqFailureCode = code.Replace(' ', '_')
                    }
                }
            }
        });
    }

    /// <summary>
    /// Explicit source root must be an existing ancestor of the PBIP project.
    /// This makes SARIF artifact URIs relative to the GitHub checkout root.
    /// </summary>
    public static bool IsValidSourceRoot(string sourceRoot, string pbipPath)
    {
        try
        {
            var root = Path.GetFullPath(sourceRoot);
            var project = Path.GetDirectoryName(Path.GetFullPath(pbipPath))!;
            return Directory.Exists(root) && IsInside(root, project);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static string? RepositoryUri(string root, string projectDirectory, string relativeSource)
    {
        if (string.IsNullOrEmpty(relativeSource) || relativeSource.Contains('\\') ||
            relativeSource.StartsWith("/", StringComparison.Ordinal) ||
            relativeSource.Split('/').Any(part => part is "" or "." or ".."))
        {
            return null;
        }
        try
        {
            var absoluteFile = Path.GetFullPath(Path.Combine(projectDirectory,
                relativeSource.Replace('/', Path.DirectorySeparatorChar)));
            if (!IsInside(root, absoluteFile))
            {
                return null;
            }
            var relative = Path.GetRelativePath(root, absoluteFile).Replace('\\', '/');
            return string.Join("/", relative.Split('/').Select(Uri.EscapeDataString));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static bool IsInside(string root, string candidate)
    {
        var path = Path.GetRelativePath(root, candidate).Replace('\\', '/');
        return path != ".." && !path.StartsWith("../", StringComparison.Ordinal) &&
               !Path.IsPathRooted(path);
    }

    private static string Level(DiagnosticSeverity severity) => severity switch
    {
        DiagnosticSeverity.Error => "error",
        DiagnosticSeverity.Warning => "warning",
        _ => "note"
    };

    private static string ToolVersion() =>
        typeof(AuditSarifRenderer).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? "0.3.0-alpha.1";

    private static void Write(TextWriter writer, object report)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(report, Options));
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            json.WriteStartObject();
            json.WriteString("$schema", Schema);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                property.WriteTo(json);
            }
            json.WriteEndObject();
        }
        writer.WriteLine(System.Text.Encoding.UTF8.GetString(stream.ToArray()));
    }
}

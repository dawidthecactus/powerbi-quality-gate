using PowerBIQualityGate.Core.Quality;

using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace PowerBIQualityGate.Cli.Configuration;

public sealed class PbiqConfigurationException : Exception
{
    public PbiqConfigurationException(string message) : base(message) { }
}

/// <summary>Exact TOM object identity, scoped to one quality rule.</summary>
public sealed record PbiqExclusion(string Rule, string Object);

public sealed record PbiqFilteredAudit(
    ModelQualityAuditResult Audit,
    int Suppressed,
    int SuppressedSm002);

/// <summary>
/// Restricted, fail-closed YAML v1 configuration. Read-only; no implicit
/// parent-directory search, environment interpolation or pattern expansion.
/// </summary>
public sealed record PbiqConfiguration(
    IReadOnlySet<string> DisabledRules,
    IReadOnlyList<PbiqExclusion> Exclusions,
    bool? FailOnWarning,
    int? MinimumMeasureCoverage)
{
    private static readonly HashSet<string> KnownRules =
        ["SM-001", "SM-002", "SM-003", "SM-004"];

    public static PbiqConfiguration Default { get; } = new(
        new HashSet<string>(StringComparer.Ordinal), [], null, null);

    /// <summary>
    /// Default discovery is beside the discovered .pbip, never in the current
    /// directory. An explicit --config path must exist.
    /// </summary>
    public static PbiqConfiguration Load(string pbipPath, string? explicitPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pbipPath);
        string file;
        try
        {
            file = explicitPath is null
                ? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(pbipPath))!, ".pbiq.yml")
                : Path.GetFullPath(explicitPath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new PbiqConfigurationException("Configuration path is invalid.");
        }

        if (!File.Exists(file))
        {
            if (explicitPath is not null)
            {
                throw new PbiqConfigurationException("Explicit configuration file does not exist.");
            }
            return Default;
        }

        string yaml;
        try
        {
            if (new FileInfo(file).Length > 65_536)
            {
                throw new PbiqConfigurationException("Configuration exceeds the 64 KiB limit.");
            }
            yaml = File.ReadAllText(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new PbiqConfigurationException("Configuration file cannot be read.");
        }

        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(yaml));
            if (stream.Documents.Count != 1)
            {
                throw new PbiqConfigurationException("Configuration must contain one YAML document.");
            }

            var root = Mapping(stream.Documents[0].RootNode, "root", ["version", "rules", "policy", "exclusions"]);
            if (!root.TryGetValue("version", out var version) || Scalar(version, "version") != "1")
            {
                throw new PbiqConfigurationException("Configuration requires version: 1.");
            }

            var disabled = new HashSet<string>(StringComparer.Ordinal);
            if (root.TryGetValue("rules", out var ruleNode))
            {
                var rules = Mapping(ruleNode, "rules", KnownRules);
                foreach (var (code, value) in rules)
                {
                    if (!Boolean(value, $"rules.{code}"))
                    {
                        disabled.Add(code);
                    }
                }
            }
            if (disabled.Count == KnownRules.Count)
            {
                throw new PbiqConfigurationException("At least one quality rule must remain enabled.");
            }

            bool? failOnWarning = null;
            int? minCoverage = null;
            if (root.TryGetValue("policy", out var policyNode))
            {
                var policy = Mapping(policyNode, "policy", ["failOnWarning", "minMeasureCoverage"]);
                if (policy.TryGetValue("failOnWarning", out var warningNode))
                {
                    failOnWarning = Boolean(warningNode, "policy.failOnWarning");
                }
                if (policy.TryGetValue("minMeasureCoverage", out var coverageNode))
                {
                    var value = Scalar(coverageNode, "policy.minMeasureCoverage");
                    if (!int.TryParse(value, System.Globalization.NumberStyles.None,
                            System.Globalization.CultureInfo.InvariantCulture, out var parsed) || parsed > 100)
                    {
                        throw new PbiqConfigurationException("policy.minMeasureCoverage must be 0..100.");
                    }
                    minCoverage = parsed;
                }
            }
            if (minCoverage is not null && disabled.Contains("SM-002"))
            {
                throw new PbiqConfigurationException("Cannot require measure coverage while SM-002 is disabled.");
            }

            var exclusions = new List<PbiqExclusion>();
            var seen = new HashSet<PbiqExclusion>();
            if (root.TryGetValue("exclusions", out var exclusionsNode))
            {
                if (exclusionsNode is not YamlSequenceNode items || items.Children.Count > 500)
                {
                    throw new PbiqConfigurationException("exclusions must be a list of at most 500 entries.");
                }
                foreach (var item in items.Children)
                {
                    var mapping = Mapping(item, "exclusions[]", ["rule", "object"]);
                    if (!mapping.TryGetValue("rule", out var ruleValue) ||
                        !mapping.TryGetValue("object", out var objectValue))
                    {
                        throw new PbiqConfigurationException("Every exclusion needs rule and object.");
                    }
                    var rule = Scalar(ruleValue, "exclusions[].rule");
                    var obj = Scalar(objectValue, "exclusions[].object");
                    if (!KnownRules.Contains(rule) || disabled.Contains(rule) ||
                        string.IsNullOrWhiteSpace(obj) || obj.Length > 512)
                    {
                        throw new PbiqConfigurationException("Exclusion has unknown/disabled rule or invalid object.");
                    }
                    var exclusion = new PbiqExclusion(rule, obj);
                    if (!seen.Add(exclusion))
                    {
                        throw new PbiqConfigurationException("Duplicate exclusion.");
                    }
                    exclusions.Add(exclusion);
                }
            }
            return new PbiqConfiguration(disabled, exclusions.AsReadOnly(), failOnWarning, minCoverage);
        }
        catch (YamlException)
        {
            // Do not leak raw YAML or user directory paths into JSON failures.
            throw new PbiqConfigurationException("Configuration contains invalid YAML syntax.");
        }
    }

    /// <summary>Filter only after validating the raw SM-002 invariant.</summary>
    public PbiqFilteredAudit ApplyExclusions(ModelQualityAuditResult original)
    {
        ArgumentNullException.ThrowIfNull(original);
        if (Exclusions.Count == 0)
        {
            return new PbiqFilteredAudit(original, 0, 0);
        }
        var active = original.Findings.Where(finding => !Exclusions.Any(exclusion =>
            string.Equals(finding.RuleCode, exclusion.Rule, StringComparison.Ordinal) &&
            string.Equals(finding.Location, exclusion.Object, StringComparison.Ordinal))).ToArray();
        var suppressedSm002 = original.Findings.Count(finding =>
            finding.RuleCode == "SM-002" && Exclusions.Any(exclusion =>
                exclusion.Rule == "SM-002" && exclusion.Object == finding.Location));
        return new PbiqFilteredAudit(
            original with { Findings = Array.AsReadOnly(active) },
            original.Findings.Count - active.Length,
            suppressedSm002);
    }

    private static Dictionary<string, YamlNode> Mapping(
        YamlNode node, string location, IEnumerable<string> allowed)
    {
        if (node is not YamlMappingNode mapping)
        {
            throw new PbiqConfigurationException($"{location} must be a mapping.");
        }
        var allowedSet = allowed.ToHashSet(StringComparer.Ordinal);
        var values = new Dictionary<string, YamlNode>(StringComparer.Ordinal);
        foreach (var pair in mapping.Children)
        {
            var key = Scalar(pair.Key, $"{location} key");
            if (!allowedSet.Contains(key) || !values.TryAdd(key, pair.Value))
            {
                throw new PbiqConfigurationException($"{location} contains an unknown or duplicate key.");
            }
        }
        return values;
    }

    private static string Scalar(YamlNode node, string location)
    {
        if (node is not YamlScalarNode scalar || scalar.Value is null)
        {
            throw new PbiqConfigurationException($"{location} must be a scalar.");
        }
        return scalar.Value;
    }

    private static bool Boolean(YamlNode node, string location)
    {
        var value = Scalar(node, location);
        if (value == "true") return true;
        if (value == "false") return false;
        throw new PbiqConfigurationException($"{location} must be true or false.");
    }
}

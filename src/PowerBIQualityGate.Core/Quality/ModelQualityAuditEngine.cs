using System.Text.RegularExpressions;

using PowerBIQualityGate.Core.Inspection;
using PowerBIQualityGate.Core.Validation;

namespace PowerBIQualityGate.Core.Quality;

/// <summary>
/// Runs registered model-quality rules against an already deserialized TOM
/// snapshot. This type performs no I/O and never modifies source files.
/// </summary>
public sealed class ModelQualityAuditEngine
{
    private static readonly Regex RuleCodePattern = new(
        @"^SM-(?!000)[0-9]{3}\z", RegexOptions.CultureInvariant);

    private readonly IModelQualityRule[] _rules;

    public ModelQualityAuditEngine(IEnumerable<IModelQualityRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        var configuredRules = rules.ToArray();
        var codes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var rule in configuredRules)
        {
            if (rule is null)
            {
                throw new ArgumentException("Rule collection cannot contain null entries.", nameof(rules));
            }

            if (string.IsNullOrWhiteSpace(rule.Code) || !RuleCodePattern.IsMatch(rule.Code))
            {
                throw new ArgumentException("Rule codes must use the SM-001 format.", nameof(rules));
            }

            if (!codes.Add(rule.Code))
            {
                throw new ArgumentException($"Duplicate quality rule code: {rule.Code}.", nameof(rules));
            }

            if (string.IsNullOrWhiteSpace(rule.Title))
            {
                throw new ArgumentException($"Rule {rule.Code} must have a title.", nameof(rules));
            }

            if (!Enum.IsDefined(rule.Severity))
            {
                throw new ArgumentException($"Rule {rule.Code} has an invalid severity.", nameof(rules));
            }
        }

        _rules = configuredRules.OrderBy(rule => rule.Code, StringComparer.Ordinal).ToArray();
    }

    public ModelQualityAuditResult Run(TomModelSnapshot model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var findings = new List<ModelQualityFinding>();

        foreach (var rule in _rules)
        {
            try
            {
                var issues = rule.Evaluate(model) ??
                    throw new InvalidOperationException("Rule returned a null issue collection.");

                foreach (var issue in issues)
                {
                    ValidateIssue(issue);
                    findings.Add(new ModelQualityFinding(
                        rule.Code,
                        rule.Title,
                        rule.Severity,
                        issue.Location,
                        issue.Message,
                        issue.WhyItMatters,
                        issue.HowToFix));
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not OperationCanceledException)
            {
                throw new ModelQualityRuleExecutionException(rule.Code, ex);
            }
        }

        var orderedFindings = findings
            .OrderBy(finding => finding.Severity)
            .ThenBy(finding => finding.RuleCode, StringComparer.Ordinal)
            .ThenBy(finding => finding.Location, StringComparer.Ordinal)
            .ThenBy(finding => finding.Message, StringComparer.Ordinal)
            .ToArray();

        return new ModelQualityAuditResult(
            model.Name,
            Array.AsReadOnly(_rules.Select(rule => rule.Code).ToArray()),
            Array.AsReadOnly(orderedFindings));
    }

    private static void ValidateIssue(ModelQualityIssue? issue)
    {
        if (issue is null ||
            string.IsNullOrWhiteSpace(issue.Location) ||
            string.IsNullOrWhiteSpace(issue.Message) ||
            string.IsNullOrWhiteSpace(issue.WhyItMatters) ||
            string.IsNullOrWhiteSpace(issue.HowToFix))
        {
            throw new InvalidOperationException(
                "Every quality issue requires a location, message, explanation, and fix.");
        }
    }
}

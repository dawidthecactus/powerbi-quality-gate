using System.CommandLine;
using System.Globalization;

using PowerBIQualityGate.Cli.Configuration;
using PowerBIQualityGate.Cli.Rendering;
using PowerBIQualityGate.Core.Discovery;
using PowerBIQualityGate.Core.Inspection;
using PowerBIQualityGate.Core.Quality;
using PowerBIQualityGate.Core.Validation;

using Spectre.Console;

namespace PowerBIQualityGate.Cli.Commands;

/// <summary>
/// Read-only orchestration of PBIP discovery, structural checks, TOM and the
/// semantic model quality rule pack. Presentation stays in the renderer.
/// </summary>
public static class AuditCommand
{
    public static Command Create()
    {
        var pathArgument = new Argument<string>("path")
        {
            Description = "Path to a PBIP file, a Fabric .SemanticModel item, or a directory containing one."
        };
        var noColorOption = new Option<bool>("--no-color")
        {
            Description = "Disable ANSI colors and control codes in audit output."
        };

        var failOnWarningOption = new Option<bool>("--fail-on-warning")
        {
            Description = "Treat one or more Warning findings as a failing quality gate (exit code 1)."
        };

        var detailsOption = new Option<bool>("--details")
        {
            Description = "Show affected objects, grouped findings and one WHY/HOW per rule."
        };
        var quietOption = new Option<bool>("--quiet")
        {
            Description = "Print a single plain-text status line (for scripts and CI)."
        };

        var minimumCoverageOption = new Option<string?>("--min-measure-coverage")
        {
            Description = "Require 0..100 percent SM-002 classification of eligible visible measures (integer)."
        };

        var formatOption = new Option<string?>("--format")
        {
            Description = "Output format: text (default), json (schema v1), or sarif (2.1.0)."
        };

        var sarifRootOption = new Option<string?>("--sarif-source-root")
        {
            Description = "Repository checkout root for SARIF source paths (must contain the source project)."
        };

        var configOption = new Option<string?>("--config")
        {
            Description = "Explicit .pbiq.yml file (default: beside the PBIP or Fabric item)."
        };

        var command = new Command("audit", "Review a local TMDL semantic model using the SM quality rules.");
        command.Arguments.Add(pathArgument);
        command.Options.Add(noColorOption);
        command.Options.Add(failOnWarningOption);
        command.Options.Add(minimumCoverageOption);
        command.Options.Add(detailsOption);
        command.Options.Add(quietOption);
        command.Options.Add(formatOption);
        command.Options.Add(configOption);
        command.Options.Add(sarifRootOption);

        command.SetAction(parseResult =>
        {
            var path = parseResult.GetValue(pathArgument) ?? string.Empty;
            var noColor = parseResult.GetValue(noColorOption) ||
                          Environment.GetEnvironmentVariable("NO_COLOR") is not null;
            var output = ConsoleFactory.Create(Console.Out, noColor || Console.IsOutputRedirected);
            var errors = ConsoleFactory.Create(Console.Error, noColor || Console.IsErrorRedirected);
            var details = parseResult.GetValue(detailsOption);
            var quiet = parseResult.GetValue(quietOption);
            var format = parseResult.GetValue(formatOption) ?? "text";
            if (!string.Equals(format, "text", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(format, "json", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(format, "sarif", StringComparison.OrdinalIgnoreCase))
            {
                new CompactAuditRenderer(errors).RenderFailure(path, "INVALID FORMAT",
                    "The --format value must be 'text', 'json', or 'sarif'.",
                    "Only documented output formats have stable contracts.",
                    "Use --format sarif, --format json, or omit the flag for text.", exitCode: 2);
                return 2;
            }
            TextWriter? jsonOutput = string.Equals(format, "json", StringComparison.OrdinalIgnoreCase)
                ? Console.Out
                : null;
            TextWriter? sarifOutput = string.Equals(format, "sarif", StringComparison.OrdinalIgnoreCase)
                ? Console.Out
                : null;
            if (jsonOutput is not null && (details || quiet))
            {
                AuditJsonRenderer.RenderFailure(jsonOutput, path, "INVALID OUTPUT MODE",
                    "--format json cannot be combined with --details or --quiet.", 2,
                    parseResult.GetValue(failOnWarningOption));
                return 2;
            }
            if (sarifOutput is not null && (details || quiet))
            {
                AuditSarifRenderer.RenderFailure(sarifOutput, "INVALID OUTPUT MODE", 2);
                return 2;
            }
            if (sarifOutput is null && parseResult.GetValue(sarifRootOption) is not null)
            {
                new CompactAuditRenderer(errors).RenderFailure(path, "INVALID OUTPUT MODE",
                    "--sarif-source-root requires --format sarif.",
                    "Source root is only meaningful for SARIF locations.",
                    "Use --format sarif or omit --sarif-source-root.", exitCode: 2);
                return 2;
            }
            if (details && quiet)
            {
                new CompactAuditRenderer(errors).RenderFailure(path, "INVALID OUTPUT MODE",
                    "--details and --quiet cannot be used together.",
                    "Output modes must be unambiguous.",
                    "Choose --details or --quiet, not both.", exitCode: 2);
                return 2;
            }
            var mode = quiet ? AuditOutputMode.Quiet : details ? AuditOutputMode.Details : AuditOutputMode.Summary;
            var thresholdText = parseResult.GetValue(minimumCoverageOption);
            int? threshold = null;
            if (thresholdText is not null)
            {
                if (!int.TryParse(thresholdText, NumberStyles.None, CultureInfo.InvariantCulture,
                    out var parsed) || parsed > 100)
                {
                    if (jsonOutput is not null)
                    {
                        AuditJsonRenderer.RenderFailure(jsonOutput, path, "INVALID COVERAGE THRESHOLD",
                            "The --min-measure-coverage value must be an integer between 0 and 100.", 2,
                            parseResult.GetValue(failOnWarningOption));
                    }
                    else if (sarifOutput is not null)
                    {
                        AuditSarifRenderer.RenderFailure(sarifOutput, "INVALID COVERAGE THRESHOLD", 2);
                    }
                    else
                    {
                        new CompactAuditRenderer(errors).RenderFailure(path, "INVALID COVERAGE THRESHOLD",
                            "The --min-measure-coverage value must be an integer between 0 and 100.",
                            "A coverage threshold must be valid before the audit can be evaluated.",
                            "For example, use --min-measure-coverage 95.", exitCode: 2, mode: mode);
                    }
                    return 2;
                }
                threshold = parsed;
            }
            return Execute(path, output, errors, parseResult.GetValue(failOnWarningOption),
                threshold, mode, jsonOutput, parseResult.GetValue(configOption),
                sarifOutput, parseResult.GetValue(sarifRootOption));
        });

        return command;
    }

    /// <summary>
    /// Execute an audit against a PBIP or Fabric Git semantic model. Injecting the
    /// consoles keeps CLI behavior testable without replacing global streams.
    /// </summary>
    public static int Execute(string path, IAnsiConsole output, IAnsiConsole errors,
        bool failOnWarning = false, int? minimumMeasureCoverage = null,
        AuditOutputMode mode = AuditOutputMode.Summary, TextWriter? jsonOutput = null,
        string? configPath = null, TextWriter? sarifOutput = null,
        string? sarifSourceRoot = null)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(errors);
        if (minimumMeasureCoverage is < 0 or > 100)
        {
            RenderFailure(errors, jsonOutput, sarifOutput, failOnWarning, minimumMeasureCoverage, path, "INVALID COVERAGE THRESHOLD",
                "The --min-measure-coverage value must be an integer between 0 and 100.",
                "A coverage threshold must be valid before the audit can be evaluated.",
                "For example, use --min-measure-coverage 95.", exitCode: 2, mode: mode);
            return 2;
        }

        var discovery = new AuditInputDiscovery().Discover(path);
        if (!discovery.IsSuccess)
        {
            RenderFailure(errors, jsonOutput, sarifOutput, failOnWarning, minimumMeasureCoverage,
                path,
                "PROJECT DISCOVERY FAILED",
                discovery.Message,
                "The audit needs one PBIP project or one Fabric semantic model.",
                "Supply one .pbip file, one .SemanticModel directory, or a folder containing one.",
                exitCode: 2, mode: mode);
            return 2;
        }

        // A real entry path is passed to all outputs; no synthetic .pbip is created.
        var entryPath = discovery.EntryPath!;

        if (sarifOutput is not null && sarifSourceRoot is not null &&
            !AuditSarifRenderer.IsValidSourceRoot(sarifSourceRoot, entryPath))
        {
            RenderFailure(errors, jsonOutput, sarifOutput, failOnWarning, minimumMeasureCoverage, path,
                "INVALID SARIF SOURCE ROOT", "The SARIF source root must be an existing ancestor of the source project.",
                "File paths must be relative to the actual source checkout.",
                "Pass --sarif-source-root with your GitHub workspace root.", exitCode: 2, mode: mode);
            return 2;
        }

        PbiqConfiguration configuration;
        try
        {
            configuration = PbiqConfiguration.Load(entryPath, configPath);
        }
        catch (PbiqConfigurationException ex)
        {
            RenderFailure(errors, jsonOutput, sarifOutput, failOnWarning, minimumMeasureCoverage, path,
                "INVALID CONFIGURATION", ex.Message,
                "An invalid quality policy cannot produce a trusted audit.",
                "Fix the .pbiq.yml file or remove --config.", exitCode: 2, mode: mode);
            return 2;
        }
        var effectiveFailOnWarning = failOnWarning || configuration.FailOnWarning == true;
        var effectiveMinimumCoverage = minimumMeasureCoverage ?? configuration.MinimumMeasureCoverage;
        if (effectiveMinimumCoverage is not null && configuration.DisabledRules.Contains("SM-002"))
        {
            RenderFailure(errors, jsonOutput, sarifOutput, effectiveFailOnWarning, effectiveMinimumCoverage, path,
                "INVALID CONFIGURATION", "Measure coverage requires SM-002 to be enabled.",
                "Coverage cannot be measured for a disabled rule.",
                "Enable SM-002 or remove the coverage threshold.", exitCode: 2, mode: mode);
            return 2;
        }

        var validator = new ProjectStructureValidator();
        var validation = discovery.Kind == AuditInputKind.FabricSemanticModel
            ? validator.ValidateFabricSemanticModel(entryPath)
            : validator.Validate(entryPath);
        if (!validation.IsValid)
        {
            RenderFailure(errors, jsonOutput, sarifOutput, failOnWarning, minimumMeasureCoverage,
                entryPath,
                "STRUCTURE VALIDATION FAILED",
                $"The project has {validation.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error)} structural error(s).",
                "The semantic model cannot be audited reliably until its source structure passes validation.",
                "Repair the semantic model or PBIP files. 'pbiq scan <path>' checks PBIP structure only.",
                exitCode: 1, mode: mode);
            return 1;
        }

        if (validation.SemanticModelFormat != SemanticModelDefinitionFormat.Tmdl ||
            string.IsNullOrWhiteSpace(validation.SemanticModelDirectory))
        {
            RenderFailure(errors, jsonOutput, sarifOutput, failOnWarning, minimumMeasureCoverage,
                entryPath,
                "LOCAL TMDL REQUIRED",
                "This audit supports local TMDL models only (not TMSL or external connections).",
                "The current rules run on an offline TOM snapshot loaded from TMDL.",
                "Save the model in TMDL project format and retry; other formats are not supported yet.",
                exitCode: 2, mode: mode);
            return 2;
        }

        var definition = Path.Combine(validation.SemanticModelDirectory, "definition");
        TomModelSnapshot snapshot;
        try
        {
            snapshot = new TomModelReader().Read(definition);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not OperationCanceledException)
        {
            RenderFailure(errors, jsonOutput, sarifOutput, failOnWarning, minimumMeasureCoverage,
                entryPath,
                "TOM DESERIALIZATION FAILED",
                $"{ex.GetType().Name}: {ex.Message}",
                "The official Microsoft parser could not load this TMDL model, so no quality rules were executed.",
                "Review TMDL syntax and model metadata. Try 'pbiq inspect <path> --tom' for a detailed inspection.",
                exitCode: 2, mode: mode);
            return 2;
        }

        ModelQualityAuditResult result;
        try
        {
            var rules = ModelQualityRulePack.CreateDefault()
                .Where(rule => !configuration.DisabledRules.Contains(rule.Code));
            var engine = new ModelQualityAuditEngine(rules);
            result = engine.Run(snapshot);
        }
        catch (ModelQualityRuleExecutionException ex)
        {
            RenderFailure(errors, jsonOutput, sarifOutput, failOnWarning, minimumMeasureCoverage,
                entryPath,
                "QUALITY RULE EXECUTION FAILED",
                $"Rule {ex.RuleCode} could not be evaluated: {ex.InnerException?.Message ?? ex.Message}",
                "An incomplete audit must not be reported as passing.",
                "Review the rule implementation and input model metadata before retrying.",
                exitCode: 2, mode: mode);
            return 2;
        }

        var coverage = ModelAuditCoverageAnalyzer.Analyze(snapshot, result);
        var inconsistencies = MeasureQualityConsistencyValidator.Validate(snapshot, result, coverage);
        if (inconsistencies.Count > 0)
        {
            // Never publish a potentially contradictory PASS verdict or leak
            // object names in internal consistency diagnostics.
            RenderFailure(errors, jsonOutput, sarifOutput, failOnWarning, minimumMeasureCoverage,
                entryPath,
                "MEASURE QUALITY INTEGRITY FAILED",
                $"SM-002 aggregate invariants failed: {string.Join(", ", inconsistencies)}.",
                "The audit's measure-quality metadata cannot be trusted.",
                "Review the inspector and assessment calculations before rerunning the audit.",
                exitCode: 2, mode: mode);
            return 2;
        }
        // Validate SM-002 against raw evaluated findings, then apply exact
        // object exclusions before the quality gate evaluates visible findings.
        var filtered = configuration.ApplyExclusions(result);
        result = filtered.Audit;
        var decision = new CoverageAwareGatePolicy(effectiveFailOnWarning, effectiveMinimumCoverage)
            .Evaluate(result, coverage);
        if (jsonOutput is not null)
        {
            AuditJsonRenderer.RenderSuccess(jsonOutput, entryPath, snapshot, result,
                decision, coverage, definition, filtered.Suppressed);
            return decision.ExitCode;
        }

        if (sarifOutput is not null)
        {
            AuditSarifRenderer.RenderSuccess(sarifOutput, entryPath, snapshot, result,
                decision, definition, sarifSourceRoot);
            return decision.ExitCode;
        }

        var console = decision.ExitCode == 0 ? output : errors;
        new CompactAuditRenderer(console).Render(entryPath, snapshot, result,
            decision, mode, coverage, filtered.Suppressed, filtered.SuppressedSm002);
        return decision.ExitCode;
    }

    private static void RenderFailure(
        IAnsiConsole errors, TextWriter? jsonOutput, TextWriter? sarifOutput, bool failOnWarning,
        int? minimumMeasureCoverage, string path, string title, string message,
        string why, string fix, int exitCode, AuditOutputMode mode = AuditOutputMode.Summary)
    {
        if (jsonOutput is not null)
        {
            AuditJsonRenderer.RenderFailure(jsonOutput, path, title, message, exitCode,
                failOnWarning, minimumMeasureCoverage);
            return;
        }

        if (sarifOutput is not null)
        {
            AuditSarifRenderer.RenderFailure(sarifOutput, title, exitCode);
            return;
        }
        new CompactAuditRenderer(errors).RenderFailure(path, title, message, why, fix,
            exitCode, mode);
    }
}

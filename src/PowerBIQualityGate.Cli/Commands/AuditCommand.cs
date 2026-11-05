using System.CommandLine;
using System.Globalization;

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
            Description = "Path to a PBIP file or a directory containing one PBIP file."
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

        var command = new Command("audit", "Review a local TMDL semantic model using the SM quality rules.");
        command.Arguments.Add(pathArgument);
        command.Options.Add(noColorOption);
        command.Options.Add(failOnWarningOption);
        command.Options.Add(minimumCoverageOption);
        command.Options.Add(detailsOption);
        command.Options.Add(quietOption);

        command.SetAction(parseResult =>
        {
            var path = parseResult.GetValue(pathArgument) ?? string.Empty;
            var noColor = parseResult.GetValue(noColorOption) ||
                          Environment.GetEnvironmentVariable("NO_COLOR") is not null;
            var output = ConsoleFactory.Create(Console.Out, noColor || Console.IsOutputRedirected);
            var errors = ConsoleFactory.Create(Console.Error, noColor || Console.IsErrorRedirected);
            var details = parseResult.GetValue(detailsOption);
            var quiet = parseResult.GetValue(quietOption);
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
                    new CompactAuditRenderer(errors).RenderFailure(path, "INVALID COVERAGE THRESHOLD",
                        "The --min-measure-coverage value must be an integer between 0 and 100.",
                        "A coverage threshold must be valid before the audit can be evaluated.",
                        "For example, use --min-measure-coverage 95.", exitCode: 2, mode: mode);
                    return 2;
                }
                threshold = parsed;
            }
            return Execute(path, output, errors, parseResult.GetValue(failOnWarningOption), threshold, mode);
        });

        return command;
    }

    /// <summary>
    /// Execute an audit against a discovered PBIP entry point. Injecting the
    /// consoles keeps CLI behavior testable without replacing global streams.
    /// </summary>
    public static int Execute(string path, IAnsiConsole output, IAnsiConsole errors,
        bool failOnWarning = false, int? minimumMeasureCoverage = null,
        AuditOutputMode mode = AuditOutputMode.Summary)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(errors);
        if (minimumMeasureCoverage is < 0 or > 100)
        {
            new CompactAuditRenderer(errors).RenderFailure(path, "INVALID COVERAGE THRESHOLD",
                "The --min-measure-coverage value must be an integer between 0 and 100.",
                "A coverage threshold must be valid before the audit can be evaluated.",
                "For example, use --min-measure-coverage 95.", exitCode: 2, mode: mode);
            return 2;
        }

        var discovery = new PbipProjectDiscovery().Discover(path);
        if (!discovery.IsSuccess)
        {
            new CompactAuditRenderer(errors).RenderFailure(
                path,
                "PROJECT DISCOVERY FAILED",
                discovery.Message,
                "The audit needs exactly one readable PBIP entry point.",
                "Supply an existing .pbip file or a folder containing exactly one .pbip file.",
                exitCode: 2, mode: mode);
            return 2;
        }

        var validation = new ProjectStructureValidator().Validate(discovery.PbipPath!);
        if (!validation.IsValid)
        {
            new CompactAuditRenderer(errors).RenderFailure(
                discovery.PbipPath!,
                "STRUCTURE VALIDATION FAILED",
                $"The project has {validation.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error)} structural error(s).",
                "The semantic model cannot be audited reliably until the PBIP structure passes validation.",
                "Run 'pbiq scan <path>' to review PBI diagnostics and repair the project first.",
                exitCode: 1, mode: mode);
            return 1;
        }

        if (validation.SemanticModelFormat != SemanticModelDefinitionFormat.Tmdl ||
            string.IsNullOrWhiteSpace(validation.SemanticModelDirectory))
        {
            new CompactAuditRenderer(errors).RenderFailure(
                discovery.PbipPath!,
                "LOCAL TMDL REQUIRED",
                "This audit supports local TMDL models only (not TMSL or external connections).",
                "The current rules run on an offline TOM snapshot loaded from TMDL.",
                "Save the model in TMDL project format and retry; other formats are not supported yet.",
                exitCode: 2, mode: mode);
            return 2;
        }

        TomModelSnapshot snapshot;
        try
        {
            var definition = Path.Combine(validation.SemanticModelDirectory, "definition");
            snapshot = new TomModelReader().Read(definition);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not OperationCanceledException)
        {
            new CompactAuditRenderer(errors).RenderFailure(
                discovery.PbipPath!,
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
            var engine = new ModelQualityAuditEngine(ModelQualityRulePack.CreateDefault());
            result = engine.Run(snapshot);
        }
        catch (ModelQualityRuleExecutionException ex)
        {
            new CompactAuditRenderer(errors).RenderFailure(
                discovery.PbipPath!,
                "QUALITY RULE EXECUTION FAILED",
                $"Rule {ex.RuleCode} could not be evaluated: {ex.InnerException?.Message ?? ex.Message}",
                "An incomplete audit must not be reported as passing.",
                "Review the rule implementation and input model metadata before retrying.",
                exitCode: 2, mode: mode);
            return 2;
        }

        var coverage = ModelAuditCoverageAnalyzer.Analyze(snapshot, result);
        var decision = new CoverageAwareGatePolicy(failOnWarning, minimumMeasureCoverage)
            .Evaluate(result, coverage);
        var console = decision.ExitCode == 0 ? output : errors;
        new CompactAuditRenderer(console).Render(discovery.PbipPath!, snapshot, result, decision, mode, coverage);
        return decision.ExitCode;
    }
}

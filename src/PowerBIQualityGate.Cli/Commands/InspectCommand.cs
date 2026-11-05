using System.CommandLine;

using PowerBIQualityGate.Cli.Rendering;
using PowerBIQualityGate.Core.Discovery;
using PowerBIQualityGate.Core.Inspection;
using PowerBIQualityGate.Core.Validation;

namespace PowerBIQualityGate.Cli.Commands;

public static class InspectCommand
{
    public static Command Create()
    {
        var pathArgument = new Argument<string>("path")
        {
            Description = "Path to a PBIP file or its containing directory."
        };
        var noColorOption = new Option<bool>("--no-color")
        {
            Description = "Disable ANSI colors and control codes."
        };
        var tomOption = new Option<bool>("--tom")
        {
            Description = "Preview official Microsoft TOM/TMDL deserialization and detailed model metadata."
        };
        var measuresOption = new Option<bool>("--measures")
        {
            Description = "Summarize all TOM measure-format metadata and data types without exposing names or DAX."
        };

        var command = new Command("inspect", "Inspect a local TMDL semantic model.");
        command.Arguments.Add(pathArgument);
        command.Options.Add(noColorOption);
        command.Options.Add(tomOption);
        command.Options.Add(measuresOption);
        command.SetAction(parseResult =>
        {
            var path = parseResult.GetValue(pathArgument) ?? string.Empty;
            var noColor = parseResult.GetValue(noColorOption) ||
                          Environment.GetEnvironmentVariable("NO_COLOR") is not null;
            var useTom = parseResult.GetValue(tomOption);
            var inspectMeasures = parseResult.GetValue(measuresOption);

            if (useTom && inspectMeasures)
            {
                Console.Error.WriteLine("PBIQ inspect: --tom and --measures are alternative output modes (exit code 2).");
                return 2;
            }

            var discovery = new PbipProjectDiscovery().Discover(path);
            if (!discovery.IsSuccess)
            {
                Console.Error.WriteLine($"PBIQ inspect: {discovery.Message}");
                return 2;
            }

            var validation = new ProjectStructureValidator().Validate(discovery.PbipPath!);
            if (!validation.IsValid)
            {
                Console.Error.WriteLine("PBIQ inspect: structural validation failed. Run 'pbiq scan' for diagnostic guidance.");
                return 1;
            }

            if (validation.SemanticModelFormat != SemanticModelDefinitionFormat.Tmdl ||
                string.IsNullOrWhiteSpace(validation.SemanticModelDirectory))
            {
                Console.Error.WriteLine("PBIQ inspect: currently supports local TMDL only (not TMSL or external models).");
                return 2;
            }

            var definition = Path.Combine(validation.SemanticModelDirectory, "definition");
            if (useTom || inspectMeasures)
            {
                TomModelSnapshot snapshot;
                try
                {
                    snapshot = new TomModelReader().Read(definition);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException and not OperationCanceledException)
                {
                    Console.Error.WriteLine($"PBIQ inspect: Microsoft TOM could not deserialize this TMDL model ({ex.GetType().Name}): {ex.Message}");
                    Console.Error.WriteLine("The original files were not modified. Try 'pbiq inspect' for the declaration-only preview.");
                    return 2;
                }

                if (inspectMeasures)
                {
                    var report = new MeasureInspectionEngine().Inspect(snapshot);
                    new MeasureInspectionRenderer(Console.Out).Render(discovery.PbipPath!, report);
                }
                else
                {
                    var tomConsole = ConsoleFactory.Create(Console.Out, noColor || Console.IsOutputRedirected);
                    new ConsoleTomModelRenderer(tomConsole).Render(discovery.PbipPath!, snapshot);
                }

                return 0;
            }

            TmdlInventory inventory;
            try
            {
                inventory = new TmdlInventoryReader().Read(definition);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
            {
                Console.Error.WriteLine($"PBIQ inspect: unable to read TMDL inventory: {ex.Message}");
                return 2;
            }

            var console = ConsoleFactory.Create(Console.Out, noColor || Console.IsOutputRedirected);
            new ConsoleInventoryRenderer(console).Render(discovery.PbipPath!, inventory);
            return 0;
        });
        return command;
    }
}

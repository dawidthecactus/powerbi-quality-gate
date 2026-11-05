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

        var command = new Command("inspect", "Inspect a local TMDL semantic model.");
        command.Arguments.Add(pathArgument);
        command.Options.Add(noColorOption);
        command.Options.Add(tomOption);
        command.SetAction(parseResult =>
        {
            var path = parseResult.GetValue(pathArgument) ?? string.Empty;
            var noColor = parseResult.GetValue(noColorOption) ||
                          Environment.GetEnvironmentVariable("NO_COLOR") is not null;
            var useTom = parseResult.GetValue(tomOption);
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
            if (useTom)
            {
                TomModelSnapshot snapshot;
                try
                {
                    snapshot = new TomModelReader().Read(definition);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    Console.Error.WriteLine($"PBIQ inspect: Microsoft TOM could not deserialize this TMDL model ({ex.GetType().Name}): {ex.Message}");
                    Console.Error.WriteLine("The original files were not modified. Try 'pbiq inspect' for the declaration-only preview.");
                    return 2;
                }

                var tomConsole = ConsoleFactory.Create(Console.Out, noColor || Console.IsOutputRedirected);
                new ConsoleTomModelRenderer(tomConsole).Render(discovery.PbipPath!, snapshot);
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

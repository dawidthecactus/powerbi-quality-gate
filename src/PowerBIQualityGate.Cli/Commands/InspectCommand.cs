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

        var command = new Command("inspect", "Show a preliminary inventory of a local TMDL semantic model.");
        command.Arguments.Add(pathArgument);
        command.Options.Add(noColorOption);
        command.SetAction(parseResult =>
        {
            var path = parseResult.GetValue(pathArgument) ?? string.Empty;
            var noColor = parseResult.GetValue(noColorOption) ||
                          Environment.GetEnvironmentVariable("NO_COLOR") is not null;
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
                Console.Error.WriteLine("PBIQ inspect: this initial inspector supports local TMDL only (not TMSL or external models).");
                return 2;
            }

            TmdlInventory inventory;
            try
            {
                inventory = new TmdlInventoryReader().Read(
                    Path.Combine(validation.SemanticModelDirectory, "definition"));
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

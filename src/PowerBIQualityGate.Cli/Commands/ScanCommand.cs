using System.CommandLine;

using PowerBIQualityGate.Cli.Rendering;
using PowerBIQualityGate.Core.Discovery;
using PowerBIQualityGate.Core.Validation;

namespace PowerBIQualityGate.Cli.Commands;

public static class ScanCommand
{
    public static Command Create()
    {
        var pathArgument = new Argument<string>("path")
        {
            Description = "Path to a PBIP file or project directory."
        };
        var noColorOption = new Option<bool>("--no-color")
        {
            Description = "Disable ANSI colors and control codes in scan output."
        };

        var command = new Command("scan", "Validate the structure of a Power BI project.");
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
                var errorConsole = ConsoleFactory.Create(
                    Console.Error, noColor || Console.IsErrorRedirected);
                new ConsoleReportRenderer(errorConsole).RenderDiscoveryFailure(discovery, path);
                return 2;
            }

            var result = new ProjectStructureValidator().Validate(discovery.PbipPath!);
            var writer = result.IsValid ? Console.Out : Console.Error;
            var redirected = result.IsValid ? Console.IsOutputRedirected : Console.IsErrorRedirected;
            var console = ConsoleFactory.Create(writer, noColor || redirected);
            new ConsoleReportRenderer(console).RenderScan(discovery.PbipPath!, result);
            return result.IsValid ? 0 : 1;
        });

        return command;
    }
}

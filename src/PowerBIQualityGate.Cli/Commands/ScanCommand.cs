using System.CommandLine;
using PowerBIQualityGate.Core.Discovery;

namespace PowerBIQualityGate.Cli.Commands;

public static class ScanCommand
{
    public static Command Create()
    {
        var pathArgument = new Argument<string>("path")
        {
            Description = "Path to a PBIP file or project directory."
        };

        var command = new Command(
            "scan",
            "Discover a Power BI project.");

        command.Arguments.Add(pathArgument);

        command.SetAction(parseResult =>
        {
            var path = parseResult.GetValue(pathArgument);
            var discovery = new PbipProjectDiscovery();
            var result = discovery.Discover(path ?? "");

            if (!result.IsSuccess)
            {
                Console.Error.WriteLine(
                    $"[ERROR] {result.Message}");

                return 2;
            }

            Console.WriteLine(
                $"[OK] PBIP entry point: {result.PbipPath}");

            Console.WriteLine(
                "Project contents have not been validated yet.");

            return 0;
        });

        return command;
    }
}
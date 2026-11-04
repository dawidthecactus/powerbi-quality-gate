using System.CommandLine;

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

        var command = new Command("scan", "Validate the structure of a Power BI project.");
        command.Arguments.Add(pathArgument);

        command.SetAction(parseResult =>
        {
            var path = parseResult.GetValue(pathArgument) ?? string.Empty;
            var discovery = new PbipProjectDiscovery().Discover(path);

            if (!discovery.IsSuccess)
            {
                Console.Error.WriteLine($"[ERROR] {discovery.Message}");
                return 2;
            }

            Console.WriteLine($"[OK] PBIP entry point: {discovery.PbipPath}");
            var result = new ProjectStructureValidator().Validate(discovery.PbipPath!);

            foreach (var diagnostic in result.Diagnostics)
            {
                Console.Error.WriteLine(
                    $"[{diagnostic.Severity}] {diagnostic.Code}: {diagnostic.Message} ({diagnostic.Path})");
            }

            if (!result.IsValid)
            {
                Console.Error.WriteLine("STRUCTURE VALIDATION: FAIL");
                return 1;
            }

            Console.WriteLine($"[OK] Report format: {result.ReportFormat}");
            Console.WriteLine($"[OK] Semantic model: {result.SemanticModelFormat}");
            Console.WriteLine("STRUCTURE VALIDATION: PASS (not full schema validation)");
            return 0;
        });

        return command;
    }
}

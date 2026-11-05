using System.CommandLine;

using PowerBIQualityGate.Cli.Commands;

namespace PowerBIQualityGate.Cli;

public static class CliApplication
{
    public static RootCommand CreateRootCommand()
    {
        var rootCommand = new RootCommand(
            "Static quality analysis for Power BI projects.");

        rootCommand.Subcommands.Add(ScanCommand.Create());
        rootCommand.Subcommands.Add(InspectCommand.Create());
        rootCommand.Subcommands.Add(AuditCommand.Create());

        return rootCommand;
    }
}

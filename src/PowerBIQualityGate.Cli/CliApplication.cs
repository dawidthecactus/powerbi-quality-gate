using System.CommandLine;

namespace PowerBIQualityGate.Cli;

public static class CliApplication
{
    public static RootCommand CreateRootCommand()
    {
        var rootCommand = new RootCommand(
            "Static quality analysis for Power BI projects.");

        return rootCommand;
    }
}
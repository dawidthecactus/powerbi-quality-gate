using Spectre.Console;

namespace PowerBIQualityGate.Cli.Rendering;

public static class ConsoleFactory
{
    public static IAnsiConsole Create(TextWriter writer, bool noColor)
    {
        ArgumentNullException.ThrowIfNull(writer);

        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = noColor ? AnsiSupport.No : AnsiSupport.Detect,
            ColorSystem = noColor
                ? ColorSystemSupport.NoColors
                : ColorSystemSupport.Detect,
            Out = new AnsiConsoleOutput(writer),
            Interactive = InteractionSupport.No,
            Enrichment = new ProfileEnrichment
            {
                UseDefaultEnrichers = !noColor
            }
        });

        // Keep the output contract explicit after Spectre has built its profile.
        // Some hosts and CI environments advertise terminal capabilities via
        // environment enrichers. Plain-text mode must never inherit ANSI output.
        if (noColor)
        {
            console.Profile.Capabilities.Ansi = false;
            console.Profile.Capabilities.ColorSystem = ColorSystem.NoColors;
            console.Profile.Capabilities.Links = false;
        }

        return console;
    }
}

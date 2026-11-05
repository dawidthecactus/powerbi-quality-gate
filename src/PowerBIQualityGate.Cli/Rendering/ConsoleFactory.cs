using Spectre.Console;

namespace PowerBIQualityGate.Cli.Rendering;

public static class ConsoleFactory
{
    public static IAnsiConsole Create(TextWriter writer, bool noColor)
    {
        ArgumentNullException.ThrowIfNull(writer);

        // Even if a renderer supplies an explicit control sequence, plain
        // output must remain plain. Do not change the colored terminal path.
        var output = noColor ? new PlainTextOutputWriter(writer) : writer;
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = noColor ? AnsiSupport.No : AnsiSupport.Detect,
            ColorSystem = noColor
                ? ColorSystemSupport.NoColors
                : ColorSystemSupport.Detect,
            Out = new AnsiConsoleOutput(output),
            Interactive = InteractionSupport.No,
            Enrichment = new ProfileEnrichment
            {
                UseDefaultEnrichers = !noColor
            }
        });

        // Explicitly disable capabilities in addition to filtering the sink.
        // Some hosts advertise extra capabilities via environment enrichers.
        if (noColor)
        {
            console.Profile.Capabilities.Ansi = false;
            console.Profile.Capabilities.ColorSystem = ColorSystem.NoColors;
            console.Profile.Capabilities.Links = false;
        }

        return console;
    }
}

using Spectre.Console;

namespace PowerBIQualityGate.Cli.Rendering;

public static class ConsoleFactory
{
    public static IAnsiConsole Create(TextWriter writer, bool noColor)
    {
        ArgumentNullException.ThrowIfNull(writer);

        return AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = noColor ? AnsiSupport.No : AnsiSupport.Detect,
            ColorSystem = noColor ? ColorSystemSupport.NoColors : ColorSystemSupport.Detect,
            Out = new AnsiConsoleOutput(writer),
            Interactive = InteractionSupport.No
        });
    }
}

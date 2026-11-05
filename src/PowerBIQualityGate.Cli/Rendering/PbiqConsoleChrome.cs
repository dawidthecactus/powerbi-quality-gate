using Spectre.Console;

namespace PowerBIQualityGate.Cli.Rendering;

/// <summary>
/// Shared visual identity for the CLI. Every subcommand uses this header rather
/// than maintaining its own copy of the logo, version, and section separators.
/// </summary>
public static class PbiqConsoleChrome
{
    public const string Mint = "#8CF0B3";
    public const string Teal = "#38D3B5";
    public const string Ice = "#D9E8F0";
    public const string Muted = "#94A6B5";
    public const string Border = "#456474";

    public static void WriteBrand(IAnsiConsole console, string mode)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentException.ThrowIfNullOrWhiteSpace(mode);

        // Static and deterministic: no animations, cursor control or delays.
        console.MarkupLine($"[bold {Teal}]  ██████╗  ██████╗ ██╗ ██████╗[/]");
        console.MarkupLine($"[bold {Teal}]  ██╔══██╗██╔══██╗██║██╔═══██╗[/]");
        console.MarkupLine($"[bold {Mint}]  ██████╔╝██████╔╝██║██║   ██║[/]");
        console.MarkupLine($"[bold {Mint}]  ██╔═══╝ ██╔══██╗██║██║▄▄ ██║[/]");
        console.MarkupLine($"[bold {Mint}]  ██║     ██████╔╝██║╚██████╔╝[/]");
        console.MarkupLine($"[bold {Mint}]  ╚═╝     ╚═════╝ ╚═╝ ╚══▀▀═╝[/]");
        console.WriteLine();

        var version = typeof(PbiqConsoleChrome).Assembly.GetName().Version?.ToString(3) ?? "dev";
        console.MarkupLine($"  [bold {Mint}]PBIQ[/]  /  [bold {Ice}]POWER BI QUALITY GATE[/]  [{Muted}]v{Markup.Escape(version)}[/]");
        console.MarkupLine($"  [{Muted}]{Markup.Escape(mode)}  /  LOCAL  /  READ-ONLY[/]");
        console.WriteLine();
    }

    public static void WriteSection(IAnsiConsole console, string number, string title)
    {
        ArgumentNullException.ThrowIfNull(console);
        console.MarkupLine($"  [bold {Teal}]{Markup.Escape(number)}[/]  [bold {Ice}]{Markup.Escape(title)}[/]");
        console.MarkupLine($"  [{Border}]────────────────────────────────────────────[/]");
    }
}

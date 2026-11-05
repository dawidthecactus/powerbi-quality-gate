using PowerBIQualityGate.Core.Inspection;

using Spectre.Console;

namespace PowerBIQualityGate.Cli.Rendering;

/// <summary>Read-only presentation of a preliminary TMDL declaration inventory.</summary>
public sealed class ConsoleInventoryRenderer
{
    private readonly IAnsiConsole _console;

    public ConsoleInventoryRenderer(IAnsiConsole console)
    {
        _console = console ?? throw new ArgumentNullException(nameof(console));
    }

    public void Render(string pbipPath, TmdlInventory inventory)
    {
        ArgumentNullException.ThrowIfNull(pbipPath);
        ArgumentNullException.ThrowIfNull(inventory);

        PbiqConsoleChrome.WriteBrand(_console, "SEMANTIC MODEL INSPECTOR");
        WriteProfile(pbipPath);
        WriteSnapshot(inventory);
        WriteTables(inventory);
        WriteResult(inventory);
    }

    private void WriteProfile(string pbipPath)
    {
        PbiqConsoleChrome.WriteSection(_console, "01", "PROJECT PROFILE");
        var project = Markup.Escape(Path.GetFileName(pbipPath));
        var location = Markup.Escape(ReadablePath(pbipPath));
        var content = new Markup(
            $"[bold {PbiqConsoleChrome.Ice}]{project}[/]\n" +
            $"[{PbiqConsoleChrome.Muted}]Location[/]   {location}\n" +
            $"[{PbiqConsoleChrome.Muted}]Model[/]      [bold {PbiqConsoleChrome.Mint}]TMDL[/]\n" +
            $"[{PbiqConsoleChrome.Muted}]Scan mode[/]  Declaration inventory (offline)");

        _console.Write(new Panel(content)
            .Header("[bold]POWER BI PROJECT[/]")
            .RoundedBorder()
            .BorderColor(Color.FromHex(PbiqConsoleChrome.Border)));
        _console.WriteLine();
    }

    private void WriteSnapshot(TmdlInventory inventory)
    {
        PbiqConsoleChrome.WriteSection(_console, "02", "MODEL SNAPSHOT");
        _console.MarkupLine(
            $"  [bold {PbiqConsoleChrome.Mint}]{inventory.Tables.Count}[/] tables     " +
            $"[bold {PbiqConsoleChrome.Mint}]{inventory.Columns}[/] columns     " +
            $"[bold {PbiqConsoleChrome.Mint}]{inventory.Measures}[/] measures");
        _console.MarkupLine(
            $"  [bold {PbiqConsoleChrome.Mint}]{inventory.Relationships}[/] relationships     " +
            $"[bold {PbiqConsoleChrome.Mint}]{inventory.Hierarchies}[/] hierarchies     " +
            $"[{PbiqConsoleChrome.Muted}]{inventory.FilesRead} TMDL files read[/]");
        _console.WriteLine();
    }

    private void WriteTables(TmdlInventory inventory)
    {
        PbiqConsoleChrome.WriteSection(_console, "03", "TABLE INVENTORY");
        if (inventory.Tables.Count == 0)
        {
            _console.MarkupLine($"  [{PbiqConsoleChrome.Muted}]No table declarations found in definition/tables/.[/]");
            _console.WriteLine();
            return;
        }

        var table = new Table().RoundedBorder().BorderColor(Color.FromHex(PbiqConsoleChrome.Border));
        table.AddColumn("TABLE");
        table.AddColumn(new TableColumn("COLUMNS").RightAligned());
        table.AddColumn(new TableColumn("MEASURES").RightAligned());
        table.AddColumn(new TableColumn("HIERARCHIES").RightAligned());
        foreach (var item in inventory.Tables)
        {
            table.AddRow(
                Markup.Escape(item.Name),
                item.Columns.ToString(),
                item.Measures.ToString(),
                item.Hierarchies.ToString());
        }

        _console.Write(table);
        _console.WriteLine();
    }

    private void WriteResult(TmdlInventory inventory)
    {
        PbiqConsoleChrome.WriteSection(_console, "04", "FINAL RESULT");
        var summary = new Markup(
            $"[bold {PbiqConsoleChrome.Mint}]✓  INVENTORY: COMPLETE[/]\n" +
            $"[{PbiqConsoleChrome.Muted}]{inventory.Tables.Count} tables  ·  " +
            $"{inventory.Measures} measures  ·  {inventory.Relationships} relationships[/]\n" +
            $"[{PbiqConsoleChrome.Muted}]Read-only scan  ·  exit code: 0[/]");
        _console.Write(new Panel(summary)
            .RoundedBorder()
            .BorderColor(Color.FromHex(PbiqConsoleChrome.Mint)));
        _console.MarkupLine($"  [{PbiqConsoleChrome.Muted}]Scope: preliminary TMDL declaration counts only.[/]");
        _console.MarkupLine($"  [{PbiqConsoleChrome.Muted}]Not full TMDL grammar, model-quality or DAX validation.[/]");
    }

    private static string ReadablePath(string path)
    {
        if (!Path.IsPathRooted(path))
        {
            return path;
        }

        try
        {
            var relative = Path.GetRelativePath(Environment.CurrentDirectory, path);
            return relative != ".." &&
                   !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                   !Path.IsPathRooted(relative)
                ? relative
                : path;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path;
        }
    }
}

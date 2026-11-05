using PowerBIQualityGate.Core.Inspection;

using Spectre.Console;

namespace PowerBIQualityGate.Cli.Rendering;

/// <summary>Shared PBIQ terminal presentation for the Microsoft TOM model snapshot.</summary>
public sealed class ConsoleTomModelRenderer
{
    private const int MaxRows = 15;
    private readonly IAnsiConsole _console;

    public ConsoleTomModelRenderer(IAnsiConsole console)
    {
        _console = console ?? throw new ArgumentNullException(nameof(console));
    }

    public void Render(string pbipPath, TomModelSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(pbipPath);
        ArgumentNullException.ThrowIfNull(snapshot);

        PbiqConsoleChrome.WriteBrand(_console, "SEMANTIC MODEL INSPECTOR / TOM PREVIEW");
        WriteProfile(pbipPath, snapshot);
        WriteSnapshot(snapshot);
        WriteTables(snapshot);
        WriteColumns(snapshot);
        WriteMeasures(snapshot);
        WriteRelationships(snapshot);
        WriteResult(snapshot);
    }

    private void WriteProfile(string pbipPath, TomModelSnapshot snapshot)
    {
        PbiqConsoleChrome.WriteSection(_console, "01", "PROJECT PROFILE");
        var content = new Markup(
            $"[bold {PbiqConsoleChrome.Ice}]{Markup.Escape(Path.GetFileName(pbipPath))}[/]\n" +
            $"[{PbiqConsoleChrome.Muted}]Model[/]         {Markup.Escape(snapshot.Name)}\n" +
            $"[{PbiqConsoleChrome.Muted}]Definition[/]    [bold {PbiqConsoleChrome.Mint}]TMDL / Microsoft TOM[/]\n" +
            $"[{PbiqConsoleChrome.Muted}]Compatibility[/] {snapshot.CompatibilityLevel}\n" +
            $"[{PbiqConsoleChrome.Muted}]Scan mode[/]     Full metadata deserialization (offline)");
        _console.Write(new Panel(content)
            .Header("[bold]POWER BI PROJECT[/]")
            .RoundedBorder()
            .BorderColor(Color.FromHex(PbiqConsoleChrome.Border)));
        _console.WriteLine();
    }

    private void WriteSnapshot(TomModelSnapshot snapshot)
    {
        PbiqConsoleChrome.WriteSection(_console, "02", "MODEL SNAPSHOT");
        _console.MarkupLine(
            $"  [bold {PbiqConsoleChrome.Mint}]{snapshot.Tables.Count}[/] tables    " +
            $"[bold {PbiqConsoleChrome.Mint}]{snapshot.Columns}[/] columns    " +
            $"[bold {PbiqConsoleChrome.Mint}]{snapshot.Measures}[/] measures");
        _console.MarkupLine(
            $"  [bold {PbiqConsoleChrome.Mint}]{snapshot.Relationships.Count}[/] relationships    " +
            $"[bold {PbiqConsoleChrome.Mint}]{snapshot.Hierarchies}[/] hierarchies    " +
            $"[bold {PbiqConsoleChrome.Mint}]{snapshot.Partitions}[/] partitions");
        _console.WriteLine();
    }

    private void WriteTables(TomModelSnapshot snapshot)
    {
        PbiqConsoleChrome.WriteSection(_console, "03", "TABLE INVENTORY");
        var table = NewTable("TABLE", "COLUMNS", "MEASURES", "PARTITIONS");
        foreach (var item in snapshot.Tables.Take(MaxRows))
        {
            table.AddRow(
                Markup.Escape(item.Name + (item.IsHidden ? " [hidden]" : "")),
                item.Columns.Count.ToString(),
                item.Measures.Count.ToString(),
                item.Partitions.ToString());
        }
        WriteTableOrEmpty(table, snapshot.Tables.Count, "No tables in TOM model.");
    }

    private void WriteColumns(TomModelSnapshot snapshot)
    {
        PbiqConsoleChrome.WriteSection(_console, "04", "COLUMN CATALOG");
        var columns = snapshot.Tables
            .SelectMany(table => table.Columns.Select(column => (Table: table.Name, Column: column)))
            .ToArray();
        var tableView = NewTable("TABLE", "COLUMN", "DATA TYPE", "KIND");
        foreach (var item in columns.Take(MaxRows))
        {
            tableView.AddRow(
                Markup.Escape(item.Table),
                Markup.Escape(item.Column.Name + (item.Column.IsHidden ? " [hidden]" : "")),
                Markup.Escape(item.Column.DataType),
                Markup.Escape(item.Column.Kind));
        }
        WriteTableOrEmpty(tableView, columns.Length, "No columns in TOM model.");
    }

    private void WriteMeasures(TomModelSnapshot snapshot)
    {
        PbiqConsoleChrome.WriteSection(_console, "05", "MEASURE CATALOG / DAX PREVIEW");
        var measures = snapshot.Tables
            .SelectMany(table => table.Measures.Select(measure => (Table: table.Name, Measure: measure)))
            .ToArray();
        var tableView = NewTable("TABLE", "MEASURE", "EXPRESSION (PREVIEW)");
        foreach (var item in measures.Take(MaxRows))
        {
            tableView.AddRow(
                Markup.Escape(item.Table),
                Markup.Escape(item.Measure.Name + (item.Measure.IsHidden ? " [hidden]" : "")),
                Markup.Escape(Preview(item.Measure.Expression, 72)));
        }
        WriteTableOrEmpty(tableView, measures.Length, "No measures in TOM model.");
        _console.MarkupLine($"  [{PbiqConsoleChrome.Muted}]Expressions are displayed, not evaluated or checked for DAX correctness.[/]");
        _console.WriteLine();
    }

    private void WriteRelationships(TomModelSnapshot snapshot)
    {
        PbiqConsoleChrome.WriteSection(_console, "06", "RELATIONSHIP MAP");
        var table = NewTable("FROM", "TO", "ACTIVE", "CROSS FILTER");
        foreach (var item in snapshot.Relationships.Take(MaxRows))
        {
            table.AddRow(
                Markup.Escape(item.From),
                Markup.Escape(item.To),
                item.IsActive ? "Yes" : "No",
                Markup.Escape(item.CrossFilterDirection));
        }
        WriteTableOrEmpty(table, snapshot.Relationships.Count, "No relationships in TOM model.");
    }

    private void WriteResult(TomModelSnapshot snapshot)
    {
        PbiqConsoleChrome.WriteSection(_console, "07", "FINAL RESULT");
        var summary = new Markup(
            $"[bold {PbiqConsoleChrome.Mint}]✓  TOM DESERIALIZATION: COMPLETE[/]\n" +
            $"[{PbiqConsoleChrome.Muted}]{snapshot.Tables.Count} tables  ·  " +
            $"{snapshot.Measures} measures  ·  {snapshot.Relationships.Count} relationships[/]\n" +
            $"[{PbiqConsoleChrome.Muted}]Read-only inspection  ·  exit code: 0[/]");
        _console.Write(new Panel(summary)
            .RoundedBorder()
            .BorderColor(Color.FromHex(PbiqConsoleChrome.Mint)));
        _console.MarkupLine($"  [{PbiqConsoleChrome.Muted}]Scope: TMDL deserialized into TOM metadata.[/]");
        _console.MarkupLine($"  [{PbiqConsoleChrome.Muted}]No DAX, business logic, data refresh or full model-quality validation.[/]");
    }

    private Table NewTable(params string[] headers)
    {
        var table = new Table().RoundedBorder().BorderColor(Color.FromHex(PbiqConsoleChrome.Border));
        foreach (var header in headers)
        {
            table.AddColumn(header);
        }
        return table;
    }

    private void WriteTableOrEmpty(Table table, int total, string emptyText)
    {
        if (total == 0)
        {
            _console.MarkupLine($"  [{PbiqConsoleChrome.Muted}]{Markup.Escape(emptyText)}[/]");
        }
        else
        {
            _console.Write(table);
            if (total > MaxRows)
            {
                _console.MarkupLine($"  [{PbiqConsoleChrome.Muted}]Showing first {MaxRows} of {total} items.[/]");
            }
        }
        _console.WriteLine();
    }

    private static string Preview(string? expression, int limit)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return "(empty)";
        }

        var plain = string.Join(' ', expression.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return plain.Length <= limit ? plain : plain[..(limit - 3)] + "...";
    }
}

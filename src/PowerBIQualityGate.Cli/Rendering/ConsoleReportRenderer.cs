using PowerBIQualityGate.Core.Discovery;
using PowerBIQualityGate.Core.Validation;

using Spectre.Console;

namespace PowerBIQualityGate.Cli.Rendering;

/// <summary>
/// Branded, read-only presentation of structural scan results.
/// No discovery, validation, or project mutation occurs in this type.
/// </summary>
public sealed class ConsoleReportRenderer
{
    private const string Mint = "#8CF0B3";
    private const string Teal = "#38D3B5";
    private const string Ice = "#D9E8F0";
    private const string Muted = "#94A6B5";
    private const string Border = "#456474";
    private const string Red = "#FF7373";
    private const string Amber = "#F6CD71";

    private readonly IAnsiConsole _console;

    public ConsoleReportRenderer(IAnsiConsole console)
    {
        _console = console ?? throw new ArgumentNullException(nameof(console));
    }

    public void RenderDiscoveryFailure(ProjectDiscoveryResult discovery, string? inputPath = null)
    {
        ArgumentNullException.ThrowIfNull(discovery);

        WriteBrand();
        WriteSection("01", "PROJECT PROFILE");
        _console.MarkupLine($"  [{Muted}]Entry point[/]      [bold {Red}]NOT FOUND[/]");
        _console.MarkupLine($"  [{Muted}]Discovery state[/]  {Markup.Escape(discovery.Status.ToString())}");
        if (!string.IsNullOrWhiteSpace(inputPath))
        {
            _console.MarkupLine($"  [{Muted}]Requested path[/]   {Markup.Escape(inputPath)}");
        }
        _console.WriteLine();

        WriteSection("02", "DIAGNOSTIC REPORT");
        _console.MarkupLine($"  [bold {Red}]Project discovery failed[/]");
        _console.WriteLine();
        WriteFinding(
            "DISCOVERY",
            "ERROR",
            discovery.Message,
            "(input path)",
            DiagnosticGuidance.ForDiscovery(discovery.Status));

        WriteOutcome(false, 2, 1, 0, 0, "DISCOVERY");
    }

    public void RenderScan(string pbipPath, ProjectStructureValidationResult result)
    {
        ArgumentNullException.ThrowIfNull(pbipPath);
        ArgumentNullException.ThrowIfNull(result);

        var diagnostics = result.Diagnostics
            .OrderBy(d => d.Severity == DiagnosticSeverity.Error ? 0 : d.Severity == DiagnosticSeverity.Warning ? 1 : 2)
            .ThenBy(d => d.Code, StringComparer.Ordinal)
            .ThenBy(d => d.Path, StringComparer.Ordinal)
            .ToArray();

        WriteBrand();
        WriteProjectProfile(pbipPath, result);
        WritePipeline(result);
        WriteDiagnostics(diagnostics);

        var errors = diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);
        var warnings = diagnostics.Count(d => d.Severity == DiagnosticSeverity.Warning);
        var info = diagnostics.Count(d => d.Severity == DiagnosticSeverity.Information);
        WriteOutcome(result.IsValid, result.IsValid ? 0 : 1, errors, warnings, info, "STRUCTURE VALIDATION");
    }

    private void WriteBrand() => PbiqConsoleChrome.WriteBrand(_console, "STRUCTURE SCANNER");

    private void WriteProjectProfile(string pbipPath, ProjectStructureValidationResult result)
    {
        WriteSection("01", "PROJECT PROFILE");

        var name = Markup.Escape(Path.GetFileName(pbipPath));
        var location = Markup.Escape(ReadablePath(pbipPath));
        var report = ReportFormat(result.ReportFormat);
        var model = ModelFormat(result.SemanticModelFormat);
        var content = new Markup(
            $"[bold {Ice}]{name}[/]\n" +
            $"[{Muted}]Location[/]   {location}\n" +
            $"[{Muted}]Report[/]     {report}\n" +
            $"[{Muted}]Model[/]      {model}\n" +
            $"[{Muted}]Scan mode[/]  Structural integrity (offline)");

        _console.Write(new Panel(content)
            .Header("[bold]POWER BI PROJECT[/]")
            .RoundedBorder()
            .BorderColor(Color.FromHex(Border)));
        _console.WriteLine();
    }

    private void WritePipeline(ProjectStructureValidationResult result)
    {
        WriteSection("02", "SCAN CHECKPOINTS");
        WriteCheckpoint("01", "PBIP discovery", "ENTRY POINT FOUND", Mint, "✓");
        WriteCheckpoint(
            "02", "Report layout",
            result.ReportFormat == ReportDefinitionFormat.Unknown ? "NOT RESOLVED" : $"DETECTED / {ReportFormatPlain(result.ReportFormat)}",
            result.ReportFormat == ReportDefinitionFormat.Unknown ? Muted : Mint,
            result.ReportFormat == ReportDefinitionFormat.Unknown ? "·" : "✓");
        WriteCheckpoint(
            "03", "Semantic model",
            result.SemanticModelFormat == SemanticModelDefinitionFormat.Unknown ? "NOT RESOLVED" : $"DETECTED / {ModelFormatPlain(result.SemanticModelFormat)}",
            result.SemanticModelFormat == SemanticModelDefinitionFormat.Unknown ? Muted : Mint,
            result.SemanticModelFormat == SemanticModelDefinitionFormat.Unknown ? "·" : "✓");
        _console.WriteLine();
        _console.MarkupLine($"  [{Muted}]Detected formats do not imply full schema or DAX validation.[/]");
        _console.WriteLine();
    }

    private void WriteDiagnostics(IReadOnlyList<ProjectDiagnostic> diagnostics)
    {
        WriteSection("03", "DIAGNOSTIC REPORT");
        if (diagnostics.Count == 0)
        {
            _console.MarkupLine($"  [bold {Mint}]✓  No structural issues detected[/]");
            _console.MarkupLine($"  [{Muted}]The implemented structure checks found no errors.[/]");
            _console.WriteLine();
            return;
        }

        foreach (var diagnostic in diagnostics)
        {
            var severity = diagnostic.Severity switch
            {
                DiagnosticSeverity.Error => "ERROR",
                DiagnosticSeverity.Warning => "WARNING",
                _ => "INFO"
            };
            var advice = DiagnosticGuidance.ForCode(diagnostic.Code);
            WriteFinding(diagnostic.Code, severity, diagnostic.Message, diagnostic.Path, advice);
        }
    }

    private void WriteFinding(string code, string severity, string message, string path, DiagnosticGuidance.Advice advice)
    {
        var color = severity switch
        {
            "ERROR" => Red,
            "WARNING" => Amber,
            _ => Muted
        };
        var symbol = severity switch
        {
            "ERROR" => "✕",
            "WARNING" => "!",
            _ => "i"
        };

        _console.MarkupLine($"  [bold {color}]{symbol}  {Markup.Escape(severity)}[/]  [bold {Ice}]{Markup.Escape(code)}[/]  [{Muted}]/ {Markup.Escape(advice.Area)}[/]");
        _console.MarkupLine($"     [bold]{Markup.Escape(message)}[/]");
        _console.WriteLine();
        _console.MarkupLine($"     [bold {Amber}]WHY IT MATTERS[/]");
        _console.MarkupLine($"     {Markup.Escape(advice.Why)}");
        _console.WriteLine();
        _console.MarkupLine($"     [bold {Teal}]HOW TO FIX[/]");
        _console.MarkupLine($"     {Markup.Escape(advice.Fix)}");
        _console.MarkupLine($"     [{Muted}]File: {Markup.Escape(ReadablePath(path))}[/]");
        _console.WriteLine();
    }

    private void WriteOutcome(bool passed, int exitCode, int errors, int warnings, int info, string scope)
    {
        WriteSection("04", "FINAL RESULT");
        var color = passed ? Mint : Red;
        var word = passed ? "PASS" : "FAIL";
        var symbol = passed ? "✓" : "✕";
        var status = passed ? "Completed without structural errors" : "Structural issues require attention";

        var summary = new Markup(
            $"[bold {color}]{symbol}  {Markup.Escape(scope)}: {word}[/]\n" +
            $"[bold {Red}]{Count(errors, "error", "errors")}[/]   " +
            $"[bold {Amber}]{Count(warnings, "warning", "warnings")}[/]   " +
            $"[{Muted}]{info} info[/]\n" +
            $"[{Muted}]{status}  ·  exit code: {exitCode}[/]");

        _console.Write(new Panel(summary)
            .RoundedBorder()
            .BorderColor(Color.FromHex(color)));
        _console.MarkupLine($"  [{Muted}]Scope: structural checks only.[/]");
        _console.MarkupLine($"  [{Muted}]No DAX, model-quality or full schema validation.[/]");
    }

    private void WriteSection(string number, string title) =>
        PbiqConsoleChrome.WriteSection(_console, number, title);

    private void WriteCheckpoint(string index, string label, string description, string color, string symbol)
    {
        _console.MarkupLine(
            $"  [{Muted}]{Markup.Escape(index)}[/]   [bold {color}]{symbol}[/]  " +
            $"{Markup.Escape(label.PadRight(18))} [{color}]{Markup.Escape(description)}[/]");
    }

    private static string ReadablePath(string path)
    {
        // Prefer a short relative path for projects below the current directory.
        // Fall back to the original path if it is outside the current directory.
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

    private static string Count(int count, string singular, string plural) =>
        $"{count} {(count == 1 ? singular : plural)}";

    private static string ReportFormat(ReportDefinitionFormat format) => format switch
    {
        ReportDefinitionFormat.Pbir => $"[bold {Mint}]PBIR[/]",
        ReportDefinitionFormat.PbirLegacy => $"[bold {Mint}]PBIR-Legacy[/]",
        _ => $"[{Muted}]Not resolved[/]"
    };

    private static string ModelFormat(SemanticModelDefinitionFormat format) => format switch
    {
        SemanticModelDefinitionFormat.Tmdl => $"[bold {Mint}]TMDL[/]",
        SemanticModelDefinitionFormat.Tmsl => $"[bold {Mint}]TMSL[/]",
        SemanticModelDefinitionFormat.External => $"[bold {Teal}]External connection[/]",
        _ => $"[{Muted}]Not resolved[/]"
    };

    private static string ReportFormatPlain(ReportDefinitionFormat format) => format switch
    {
        ReportDefinitionFormat.Pbir => "PBIR",
        ReportDefinitionFormat.PbirLegacy => "PBIR-Legacy",
        _ => "Not resolved"
    };

    private static string ModelFormatPlain(SemanticModelDefinitionFormat format) => format switch
    {
        SemanticModelDefinitionFormat.Tmdl => "TMDL",
        SemanticModelDefinitionFormat.Tmsl => "TMSL",
        SemanticModelDefinitionFormat.External => "External connection",
        _ => "Not resolved"
    };
}

using PowerBIQualityGate.Cli.Rendering;
using PowerBIQualityGate.Core.Inspection;

using Spectre.Console.Testing;

namespace PowerBIQualityGate.Tests;

public sealed class ConsoleTomModelRendererTests
{
    [Fact]
    public void TomPreview_UsesSharedBrandAndCatalogs()
    {
        using var console = new TestConsole();
        console.Profile.Width = 120;

        new ConsoleTomModelRenderer(console).Render("Sample.pbip", Sample());

        Assert.Contains("PBIQ", console.Output);
        Assert.Contains("SEMANTIC MODEL INSPECTOR / TOM PREVIEW", console.Output);
        Assert.Contains("PROJECT PROFILE", console.Output);
        Assert.Contains("MODEL SNAPSHOT", console.Output);
        Assert.Contains("COLUMN CATALOG", console.Output);
        Assert.Contains("MEASURE CATALOG", console.Output);
        Assert.Contains("RELATIONSHIP MAP", console.Output);
        Assert.Contains("TOM DESERIALIZATION: COMPLETE", console.Output);
        Assert.Contains("SUM", console.Output);
        Assert.Contains("Sales[ProductId]", console.Output);
    }

    [Fact]
    public void TomPreview_DoesNotClaimDaxWasValidated()
    {
        using var console = new TestConsole();

        new ConsoleTomModelRenderer(console).Render("Sample.pbip", Sample());

        Assert.Contains("No DAX", console.Output);
        Assert.Contains("not evaluated", console.Output);
    }

    [Fact]
    public void TomPreview_EscapesUntrustedMetadata()
    {
        using var console = new TestConsole();
        var sample = new TomModelSnapshot(
            "[red]Model[/]", 1600,
            [new TomTableSnapshot("[red]Sales[/]", false,
                [new TomColumnSnapshot("[green]Amount[/]", "Decimal", "Data", false, null)],
                [new TomMeasureSnapshot("[blue]Revenue[/]", "[yellow]42[/]", null, false)], 0, 0)],
            []);

        new ConsoleTomModelRenderer(console).Render("[red]Sample.pbip", sample);

        Assert.Contains("[red]Sales[/]", console.Output);
        Assert.Contains("[blue]Revenue[/]", console.Output);
        Assert.Contains("[green]Amount[/]", console.Output);
    }

    [Fact]
    public void TomPreview_NoColorDoesNotEmitAnsiCodes()
    {
        using var writer = new StringWriter();
        var console = ConsoleFactory.Create(writer, noColor: true);

        new ConsoleTomModelRenderer(console).Render("Sample.pbip", Sample());

        var output = writer.ToString();
        Assert.Contains("TOM DESERIALIZATION: COMPLETE", output);
        Assert.False(console.Profile.Capabilities.Ansi);

        var escapeIndex = output.IndexOf("\u001b[", StringComparison.Ordinal);
        var context = escapeIndex < 0
            ? string.Empty
            : string.Join(" ", output[
                Math.Max(0, escapeIndex - 16)..Math.Min(output.Length, escapeIndex + 24)]
                .Select(character => $"U+{(int)character:X4}"));
        Assert.True(escapeIndex < 0,
            $"Unexpected ANSI escape at offset {escapeIndex}. Nearby code points: {context}");
    }

    [Fact]
    public void TomPreview_LimitsLongTableLists()
    {
        using var console = new TestConsole();
        var tables = Enumerable.Range(1, 17)
            .Select(n => new TomTableSnapshot($"T{n:00}", false, [], [], 0, 0))
            .ToArray();

        new ConsoleTomModelRenderer(console).Render(
            "Sample.pbip", new TomModelSnapshot("Model", 1600, tables, []));

        Assert.Contains("Showing first 15 of 17 items", console.Output);
    }

    private static TomModelSnapshot Sample() => new(
        "Model", 1600,
        [new TomTableSnapshot("Sales", false,
            [new TomColumnSnapshot("ProductId", "Int64", "Data", false, null)],
            [new TomMeasureSnapshot("Revenue", "SUM(Sales[Amount])", "0.00", false)], 0, 1)],
        [new TomRelationshipSnapshot("r1", "Sales[ProductId]", "Product[ProductId]", true, "OneDirection")]);
}

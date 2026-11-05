using PowerBIQualityGate.Cli.Rendering;
using PowerBIQualityGate.Core.Inspection;

using Spectre.Console.Testing;

namespace PowerBIQualityGate.Tests;

public sealed class ConsoleInventoryRendererTests
{
    [Fact]
    public void Inspect_UsesSharedBrandedLayoutAndSections()
    {
        using var console = new TestConsole();
        var inventory = SampleInventory();

        new ConsoleInventoryRenderer(console).Render("Example.pbip", inventory);

        Assert.Contains("PBIQ", console.Output);
        Assert.Contains("POWER BI QUALITY GATE", console.Output);
        Assert.Contains("SEMANTIC MODEL INSPECTOR", console.Output);
        Assert.Contains("PROJECT PROFILE", console.Output);
        Assert.Contains("MODEL SNAPSHOT", console.Output);
        Assert.Contains("TABLE INVENTORY", console.Output);
        Assert.Contains("FINAL RESULT", console.Output);
        Assert.Contains("Product", console.Output);
        Assert.Contains("Sales", console.Output);
        Assert.Contains("INVENTORY: COMPLETE", console.Output);
    }

    [Fact]
    public void Inspect_ClearlyLimitsScopeToDeclarationCounting()
    {
        using var console = new TestConsole();

        new ConsoleInventoryRenderer(console).Render("Example.pbip", SampleInventory());

        Assert.Contains("preliminary TMDL declaration counts", console.Output);
        Assert.Contains("Not full TMDL grammar", console.Output);
        Assert.DoesNotContain("STRUCTURE VALIDATION: PASS", console.Output);
    }

    [Fact]
    public void Inspect_EmptyInventory_HasReadableSummary()
    {
        using var console = new TestConsole();
        var inventory = new TmdlInventory(Array.Empty<TmdlTableInventory>(), 0, 0);

        new ConsoleInventoryRenderer(console).Render("Empty.pbip", inventory);

        Assert.Contains("No table declarations found", console.Output);
        Assert.Contains("INVENTORY: COMPLETE", console.Output);
    }

    [Fact]
    public void Inspect_UntrustedTableNames_AreEscaped()
    {
        using var console = new TestConsole();
        var inventory = new TmdlInventory(
            [new TmdlTableInventory("[red]Danger[/]", 1, 0, 0, "tables/danger.tmdl")], 0, 1);

        new ConsoleInventoryRenderer(console).Render("[red]Example.pbip", inventory);

        Assert.Contains("[red]Danger[/]", console.Output);
        Assert.Contains("[red]Example.pbip", console.Output);
    }

    [Fact]
    public void Inspect_NoColorMode_EmitsNoAnsiSequences()
    {
        using var writer = new StringWriter();
        var console = ConsoleFactory.Create(writer, noColor: true);

        new ConsoleInventoryRenderer(console).Render("Example.pbip", SampleInventory());

        Assert.Contains("PBIQ", writer.ToString());
        Assert.DoesNotContain("\u001b[", writer.ToString());
    }

    private static TmdlInventory SampleInventory() => new(
        [new TmdlTableInventory("Product", 2, 0, 1, "tables/Product.tmdl"),
         new TmdlTableInventory("Sales", 3, 2, 0, "tables/Sales.tmdl")],
        1, 3);
}

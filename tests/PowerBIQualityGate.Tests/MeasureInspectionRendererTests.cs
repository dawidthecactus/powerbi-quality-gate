using PowerBIQualityGate.Cli.Rendering;
using PowerBIQualityGate.Core.Inspection;

namespace PowerBIQualityGate.Tests;

public sealed class MeasureInspectionRendererTests
{
    [Fact]
    public void RendererShowsOnlyAggregatedMetadataAndNoAnsi()
    {
        var snapshot = new TomModelSnapshot("PrivateModel", 1600,
        [
            new TomTableSnapshot("SecretTable", false, [],
            [
                new TomMeasureSnapshot("SecretRevenue", "SUM ( Secret[Profit] )", "€ #,##0", false),
                new TomMeasureSnapshot("SecretMeasure", "42", null, false)
            ], 0, 0)
        ], []);
        var report = new MeasureInspectionEngine().Inspect(snapshot);
        using var writer = new StringWriter();

        new MeasureInspectionRenderer(writer).Render("C:/Private/Private.pbip", report);

        var output = writer.ToString();
        Assert.Contains("Measures inspected: 2/2", output);
        Assert.Contains("static 1", output);
        Assert.Contains("missing 1", output);
        Assert.Contains("unknown 2", output);
        Assert.DoesNotContain("SecretTable", output);
        Assert.DoesNotContain("SecretRevenue", output);
        Assert.DoesNotContain("SecretMeasure", output);
        Assert.DoesNotContain("SUM", output);
        Assert.DoesNotContain("€", output);
        Assert.DoesNotContain('\u001b', output);
    }
}

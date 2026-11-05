using PowerBIQualityGate.Core.Inspection;

namespace PowerBIQualityGate.Tests;

public sealed class TomModelReaderTests
{
    [Fact]
    public void IncludedExample_DeserializesUsingMicrosoftTom()
    {
        var definition = Path.Combine(FindExample(), "Example.SemanticModel", "definition");

        var model = new TomModelReader().Read(definition);

        Assert.Equal(2, model.Tables.Count);
        Assert.Equal(5, model.Columns);
        Assert.Equal(2, model.Measures);
        Assert.Single(model.Relationships);
        Assert.Equal(1, model.Hierarchies);
        Assert.True(model.CompatibilityLevel >= 1200);
    }

    [Fact]
    public void IncludedExample_ExposesTableAndMeasureMetadata()
    {
        var definition = Path.Combine(FindExample(), "Example.SemanticModel", "definition");

        var model = new TomModelReader().Read(definition);
        var sales = Assert.Single(model.Tables, table => table.Name == "Sales");
        var amount = Assert.Single(sales.Columns, column => column.Name == "Amount");
        var revenue = Assert.Single(sales.Measures, measure => measure.Name == "Revenue");

        Assert.Equal("Decimal", amount.DataType);
        Assert.Contains("SUM", revenue.Expression);
        Assert.Equal("Sales[ProductId]", model.Relationships[0].From);
        Assert.Equal("Product[ProductId]", model.Relationships[0].To);
    }

    [Fact]
    public void MissingTmdlFolder_ThrowsClearException()
    {
        var missing = Path.Combine(Path.GetTempPath(), "pbiq-tom-" + Guid.NewGuid().ToString("N"));

        Assert.Throws<DirectoryNotFoundException>(() => new TomModelReader().Read(missing));
    }

    private static string FindExample()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PowerBIQualityGate.slnx")))
            {
                return Path.Combine(directory.FullName, "examples", "valid-pbir-tmdl");
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}

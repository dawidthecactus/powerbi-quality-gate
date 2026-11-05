using PowerBIQualityGate.Core.Inspection;

namespace PowerBIQualityGate.Tests;

public sealed class TmdlInventoryReaderTests
{
    [Fact]
    public void EmptyModel_ReportsZeroCounts()
    {
        using var folder = new TmdlFixture();
        var result = new TmdlInventoryReader().Read(folder.Path);

        Assert.Empty(result.Tables);
        Assert.Equal(0, result.Relationships);
        Assert.Equal(0, result.FilesRead);
    }

    [Fact]
    public void CountsTableObjectsAndRelationships()
    {
        using var folder = new TmdlFixture();
        folder.Table("Sales.tmdl", """
            table Sales
            
            	column 'Order ID'
            		dataType: int64
            	column Amount
            		dataType: decimal
            	measure Revenue = SUM(Sales[Amount])
            	measure 'Revenue PY' =
            		VAR previous = 1
            		RETURN previous
            	hierarchy 'By Region'
            		level Region
            """);
        folder.Relationships("""
            relationship 1234
            	fromColumn: Sales[RegionId]
            	toColumn: Region[Id]
            relationship another
            	fromColumn: Sales[CustomerId]
            """);

        var result = new TmdlInventoryReader().Read(folder.Path);

        Assert.Single(result.Tables);
        Assert.Equal("Sales", result.Tables[0].Name);
        Assert.Equal(2, result.Columns);
        Assert.Equal(2, result.Measures);
        Assert.Equal(1, result.Hierarchies);
        Assert.Equal(2, result.Relationships);
        Assert.Equal(2, result.FilesRead);
    }

    [Fact]
    public void HandlesQuotedNamesAndAdditionalColumnTypes()
    {
        using var folder = new TmdlFixture();
        folder.Table("Sales.tmdl", """
            table 'Sales by Date'
            	calculatedColumn Quarter = QUARTER([Date])
            	calculatedTableColumn Label
            	measure 'Net Profit' = 42
            """);

        var result = new TmdlInventoryReader().Read(folder.Path);

        Assert.Equal("Sales by Date", result.Tables[0].Name);
        Assert.Equal(2, result.Columns);
        Assert.Equal(1, result.Measures);
    }

    [Fact]
    public void RejectsFilesWithoutTableDeclaration()
    {
        using var folder = new TmdlFixture();
        folder.Table("Broken.tmdl", "measure X = 1");

        Assert.Throws<FormatException>(() => new TmdlInventoryReader().Read(folder.Path));
    }

    [Fact]
    public void RejectsMultipleTableDeclarationsInSingleFile()
    {
        using var folder = new TmdlFixture();
        folder.Table("Broken.tmdl", "table First\ntable Second\n");

        Assert.Throws<FormatException>(() => new TmdlInventoryReader().Read(folder.Path));
    }

    [Fact]
    public void MissingDirectoryFailsClearly()
    {
        Assert.Throws<DirectoryNotFoundException>(() =>
            new TmdlInventoryReader().Read(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
    }

    private sealed class TmdlFixture : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "pbiq-inventory-" + Guid.NewGuid().ToString("N"));

        public TmdlFixture() => Directory.CreateDirectory(Path);

        public void Table(string file, string source)
        {
            var directory = System.IO.Path.Combine(Path, "tables");
            Directory.CreateDirectory(directory);
            File.WriteAllText(System.IO.Path.Combine(directory, file), source);
        }

        public void Relationships(string source) =>
            File.WriteAllText(System.IO.Path.Combine(Path, "relationships.tmdl"), source);

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}

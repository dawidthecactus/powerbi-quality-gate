using PowerBIQualityGate.Core.Inspection;

namespace PowerBIQualityGate.Tests;

public sealed class MeasureInspectionTmdlIntegrationTests
{
    [Fact]
    public void ExistingExampleIsInspectedWithTheRealTomSerializer()
    {
        var snapshot = new TomModelReader().Read(DefinitionFolder());
        var report = new MeasureInspectionEngine().Inspect(snapshot);

        Assert.Equal(snapshot.Measures, report.Summary.Inspected);
        Assert.Equal(snapshot.Measures, report.Summary.Visible + report.Summary.Hidden);
        Assert.Equal(report.Summary.Visible,
            report.Summary.StaticFormats + report.Summary.DynamicFormats + report.Summary.MissingFormats);
        Assert.Equal(report.Summary.Visible,
            report.Summary.NumericTypes + report.Summary.NonNumericTypes +
            report.Summary.UnknownTypes + report.Summary.UnsupportedTypes);
    }

    [Fact]
    public void RealTomSerializerReadsStaticFormatStringFromSyntheticTmdlCopy()
    {
        var source = DefinitionFolder();
        var temporary = Path.Combine(Path.GetTempPath(), "pbiq-inspection-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            foreach (var sourceFile in Directory.EnumerateFiles(source, "*.tmdl", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(source, sourceFile);
                var destination = Path.Combine(temporary, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(sourceFile, destination);
            }

            var salesFile = Path.Combine(temporary, "tables", "Sales.tmdl");
            var original = File.ReadAllText(salesFile);
            const string originalMeasure = "measure Revenue = SUM(Sales[Amount])";
            Assert.Contains(originalMeasure, original);
            File.WriteAllText(salesFile, original.Replace(originalMeasure,
                originalMeasure + "\n\t\tformatString: #,##0.00", StringComparison.Ordinal));

            var snapshot = new TomModelReader().Read(temporary);
            var report = new MeasureInspectionEngine().Inspect(snapshot);
            var revenue = Assert.Single(report.Measures, item => item.MeasureName == "Revenue");

            Assert.Equal(MeasureFormatKind.Static, revenue.FormatKind);
            Assert.Equal(snapshot.Measures, report.Summary.Total);
            Assert.Equal(1, report.Summary.StaticFormats);
            Assert.Equal(2, report.Summary.Total);
            Assert.DoesNotContain("formatString: #,##0.00", File.ReadAllText(
                Path.Combine(source, "tables", "Sales.tmdl")));
        }
        finally
        {
            Directory.Delete(temporary, recursive: true);
        }
    }

    private static string DefinitionFolder()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PowerBIQualityGate.slnx")))
            {
                return Path.Combine(directory.FullName, "examples", "valid-pbir-tmdl",
                    "Example.SemanticModel", "definition");
            }
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Solution root not found.");
    }
}

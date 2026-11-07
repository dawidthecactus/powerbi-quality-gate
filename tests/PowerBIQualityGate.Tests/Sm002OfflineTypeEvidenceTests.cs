using Microsoft.AnalysisServices.Tabular;

using PowerBIQualityGate.Core.Inspection;
using PowerBIQualityGate.Core.Quality;
using PowerBIQualityGate.Core.Quality.Rules;

namespace PowerBIQualityGate.Tests;

/// <summary>
/// Documents what Microsoft's TOM TMDL deserializer actually exposes offline.
/// All inputs are committed synthetic fixtures, never customer models.
/// </summary>
public sealed class Sm002OfflineTypeEvidenceTests
{
    [Fact]
    public void OfflineTomDoesNotResolveResultTypeForKnownNumericDaxExpressions()
    {
        var definition = DefinitionFolder();
        var database = TmdlSerializer.DeserializeDatabaseFromFolder(definition);
        var sales = database.Model!.Tables["Sales"];
        var revenue = sales.Measures["Revenue"];
        var orderCount = sales.Measures["Order Count"];

        // The referenced COLUMN has a known type, but the measure result type
        // remains unknown without a model engine evaluating DAX semantics.
        Assert.Equal("Decimal", sales.Columns["Amount"].DataType.ToString());
        Assert.Contains("SUM(", revenue.Expression, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DISTINCTCOUNT(", orderCount.Expression, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Unknown", revenue.DataType.ToString());
        Assert.Equal("Unknown", orderCount.DataType.ToString());

        // PBIQ does not introduce the Unknown value: TOM already returned it.
        var snapshot = new TomModelReader().Read(definition);
        var projected = Assert.Single(snapshot.Tables, table => table.Name == "Sales");
        Assert.All(projected.Measures, item => Assert.Equal("Unknown", item.DataType));
    }

    [Fact]
    public void AddingStaticFormatToLocalTmdlDoesNotInferNumericResultType()
    {
        var source = DefinitionFolder();
        var temporary = Path.Combine(Path.GetTempPath(),
            "pbiq-sm002-type-evidence-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);

        try
        {
            foreach (var file in Directory.EnumerateFiles(source, "*.tmdl", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(source, file);
                var copy = Path.Combine(temporary, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
                File.Copy(file, copy);
            }

            var salesFile = Path.Combine(temporary, "tables", "Sales.tmdl");
            var content = File.ReadAllText(salesFile);
            const string expression = "measure Revenue = SUM(Sales[Amount])";
            Assert.Contains(expression, content);
            File.WriteAllText(salesFile, content.Replace(expression,
                expression + "\n\t\tformatString: #,##0.00", StringComparison.Ordinal));

            var database = TmdlSerializer.DeserializeDatabaseFromFolder(temporary);
            var rawMeasure = database.Model!.Tables["Sales"].Measures["Revenue"];
            Assert.Equal("Unknown", rawMeasure.DataType.ToString());
            Assert.Equal("#,##0.00", rawMeasure.FormatString);

            var snapshot = new TomModelReader().Read(temporary);
            var inspected = new MeasureInspectionEngine().Inspect(snapshot);
            var revenue = Assert.Single(inspected.Measures, m => m.MeasureName == "Revenue");
            Assert.Equal(MeasureFormatKind.Static, revenue.FormatKind);
            Assert.Equal(MeasureDataTypeKind.Unknown, revenue.DataTypeKind);

            // The source fixture remains unchanged; this test edits only temp files.
            Assert.DoesNotContain("formatString: #,##0.00", File.ReadAllText(
                Path.Combine(source, "tables", "Sales.tmdl")));
        }
        finally
        {
            Directory.Delete(temporary, recursive: true);
        }
    }

    [Fact]
    public void ActualTmdlMissingFormatsRemainReviewItemsNotSm002Warnings()
    {
        var model = new TomModelReader().Read(DefinitionFolder());
        var inspection = new MeasureInspectionEngine().Inspect(model);
        var assessment = new MeasureFormattingAssessmentEngine().Assess(inspection);

        Assert.Equal(2, inspection.Summary.Visible);
        Assert.Equal(2, inspection.Summary.UnknownTypes);
        Assert.Equal(2, inspection.Summary.MissingFormats);
        Assert.Equal(2, assessment.Summary.UnknownTypeMissingFormat);
        Assert.Equal(2, assessment.Summary.NeedsReview);
        Assert.Equal(0, assessment.Summary.ConfirmedWarnings);
        Assert.Empty(new UnformattedNumericMeasureRule().Evaluate(model));
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

using PowerBIQualityGate.Cli.Rendering;
using PowerBIQualityGate.Core.Inspection;
using PowerBIQualityGate.Core.Quality;
using PowerBIQualityGate.Core.Quality.Rules;

namespace PowerBIQualityGate.Tests;

public sealed class StaticDaxTypeEvidenceTests
{
    [Theory]
    [InlineData("COUNTROWS('Orders')")]
    [InlineData("distinctcount ( 'Orders'[Order ID] )")]
    [InlineData("SUM('Sales'[Amount])")]
    [InlineData("AVERAGE(Sales[Amount])")]
    [InlineData("COUNTBLANK ( Data[Code] )")]
    [InlineData("DISTINCTCOUNTNOBLANK(Data[Customer])")]
    [InlineData("COUNT(Data[Units])")]
    [InlineData("42")]
    [InlineData("-12.50")]
    [InlineData(".5e+3")]
    [InlineData("COUNTROWS ( FILTER('Sales (2026)', Sales[Amount] > 0) )")]
    [InlineData("COUNTROWS('A''B')")]
    [InlineData("COUNTROWS( 'Table'[Right]]Now] )")]
    [InlineData("COUNTROWS(FILTER(Data, Data[Status] = \"a)\"))")]
    public void NarrowRootExpressionsSuggestNumericType(string expression)
    {
        Assert.Equal(StaticDaxTypeEvidenceKind.LikelyNumeric,
            StaticDaxTypeEvidence.Analyze(expression));
    }

    [Theory]
    [InlineData("TRUE()")]
    [InlineData(" false ( ) ")]
    [InlineData("\"hello\"")]
    [InlineData("\"COUNTROWS(Sales)\"")]
    [InlineData("\"a\"\"b\"")]
    [InlineData("\"\"")]
    public void LiteralExpressionsSuggestNonNumericType(string expression)
    {
        Assert.Equal(StaticDaxTypeEvidenceKind.LikelyNonNumeric,
            StaticDaxTypeEvidence.Analyze(expression));
    }

    [Theory]
    [InlineData("IF(TRUE(), 1, \"N/A\")")]
    [InlineData("SWITCH(TRUE(), TRUE(), 1, 0)")]
    [InlineData("VAR x = 1 RETURN x")]
    [InlineData("CALCULATE(SUM(Sales[Amount]))")]
    [InlineData("FORMAT(SUM(Sales[Amount]), \"0\")")]
    [InlineData("MIN('Date'[Date])")]
    [InlineData("COUNTROWS(Sales) + 1")]
    [InlineData("COUNTROWS(Sales")]
    [InlineData("COUNTROWS(Sales))")]
    [InlineData("COUNTROWS( )")]
    [InlineData("COUNTROWS(A, B)")]
    [InlineData("COUNTROWS(A) // trailing comment")]
    [InlineData("COUNTROWS(/* x */ A)")]
    [InlineData("SUM(Sales[Amount]) -- comment")]
    [InlineData("'COUNTROWS(Sales)'")]
    [InlineData("COUNTROWS(\"unclosed)")]
    [InlineData("COUNTROWS('unclosed)")]
    [InlineData("COUNTROWS(Data[unclosed)")]
    [InlineData("1 + 2")]
    [InlineData("1,000")]
    [InlineData("1e")]
    public void UnsupportedOrAmbiguousExpressionsStayUnresolved(string expression)
    {
        Assert.Equal(StaticDaxTypeEvidenceKind.Unresolved,
            StaticDaxTypeEvidence.Analyze(expression));
    }

    [Fact]
    public void EmptyAndVeryLongExpressionsStayUnresolved()
    {
        Assert.Equal(StaticDaxTypeEvidenceKind.Unresolved, StaticDaxTypeEvidence.Analyze(null));
        Assert.Equal(StaticDaxTypeEvidenceKind.Unresolved, StaticDaxTypeEvidence.Analyze("   "));
        Assert.Equal(StaticDaxTypeEvidenceKind.Unresolved,
            StaticDaxTypeEvidence.Analyze("COUNTROWS(" + new string('a', 16385) + ")"));
    }

    [Fact]
    public void EvidenceIsAdvisoryAndDoesNotChangeWarningsOrStrictGateCoverage()
    {
        var snapshot = Model(
            Measure("Count", "COUNTROWS('Data')", null),
            Measure("Text", "\"ok\"", null),
            Measure("Complex", "IF(TRUE(), 1, 0)", null),
            Measure("Formatted", "SUM(Data[Value])", "#,##0"),
            Measure("Known", "COUNTROWS('Data')", null, type: "Int64"),
            Measure("Hidden", "COUNTROWS('Data')", null, hidden: true));

        var report = new MeasureInspectionEngine().Inspect(snapshot);
        Assert.Equal(5, report.Summary.Visible);
        Assert.Equal(4, report.Summary.UnknownTypes);
        Assert.Equal(2, report.Summary.StaticDaxLikelyNumeric);
        Assert.Equal(1, report.Summary.StaticDaxLikelyNonNumeric);
        Assert.Equal(1, report.Summary.StaticDaxUnresolved);
        Assert.Equal(1, report.Summary.StaticDaxLikelyNumericMissingFormat);
        Assert.Equal(StaticDaxTypeEvidenceKind.NotAssessed,
            Assert.Single(report.Measures, x => x.MeasureName == "Known").StaticDaxTypeEvidence);
        Assert.Equal(StaticDaxTypeEvidenceKind.LikelyNumeric,
            Assert.Single(report.Measures, x => x.MeasureName == "Hidden").StaticDaxTypeEvidence);

        var findings = new UnformattedNumericMeasureRule().Evaluate(snapshot);
        Assert.Single(findings);
        Assert.Contains("Known", findings[0].Location);
        var audit = new ModelQualityAuditEngine(ModelQualityRulePack.CreateDefault()).Run(snapshot);
        var coverage = ModelAuditCoverageAnalyzer.Analyze(snapshot, audit);
        Assert.Equal(1, coverage.MeasureFormatting!.CheckedNumeric);
        Assert.Equal(4, coverage.MeasureFormatting.UnknownType);
        Assert.Equal(1, coverage.Sm002Assessment!.ConfirmedWarnings);
    }

    [Fact]
    public void AggregateRendererDoesNotExposeMeasureNamesDaxOrFormats()
    {
        var snapshot = Model(
            Measure("SecretProfit", "SUM('Private'[Sensitive Amount])", null),
            Measure("SecretLabel", "\"private\"", null),
            Measure("PrivateCalculation", "IF(TRUE(), 1, 2)", null));
        var report = new MeasureInspectionEngine().Inspect(snapshot);
        using var writer = new StringWriter();
        new MeasureInspectionRenderer(writer).Render("C:/Private/Example.pbip", report);
        var output = writer.ToString();

        Assert.Contains("Static DAX evidence (TOM unknown/unsupported)", output);
        Assert.Contains("likely numeric 1", output);
        Assert.Contains("likely nonnumeric 1", output);
        Assert.Contains("unresolved 1", output);
        Assert.Contains("missing format among likely numeric 1", output);
        Assert.Contains("advisory only", output);
        Assert.DoesNotContain("Sensitive Amount", output);
        Assert.DoesNotContain("SecretProfit", output);
        Assert.DoesNotContain("PrivateCalculation", output);
        Assert.DoesNotContain("IF(", output);
        Assert.DoesNotContain("SUM(", output);
        Assert.DoesNotContain('\u001b', output);
    }

    [Fact]
    public void RealMicrosoftTomFixtureSuppliesIndependentStaticEvidence()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "PowerBIQualityGate.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var definition = Path.Combine(dir.FullName, "examples", "valid-pbir-tmdl",
            "Example.SemanticModel", "definition");
        var snapshot = new TomModelReader().Read(definition);
        var report = new MeasureInspectionEngine().Inspect(snapshot);
        Assert.Equal(2, report.Summary.UnknownTypes);
        Assert.Equal(2, report.Summary.StaticDaxLikelyNumeric);
        Assert.Equal(2, report.Summary.StaticDaxLikelyNumericMissingFormat);
        Assert.Equal(0, report.Summary.StaticDaxUnresolved);
        Assert.Empty(new UnformattedNumericMeasureRule().Evaluate(snapshot));
    }

    private static TomModelSnapshot Model(params TomMeasureSnapshot[] measures) => new(
        "Synthetic", 1600,
        [new TomTableSnapshot("Data", false, [], measures, 0, 1)], []);

    private static TomMeasureSnapshot Measure(string name, string dax, string? format, bool hidden = false,
        string type = "Unknown") => new(name, dax, format, hidden, type);
}

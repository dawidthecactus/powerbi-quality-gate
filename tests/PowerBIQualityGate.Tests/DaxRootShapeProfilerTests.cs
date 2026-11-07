using PowerBIQualityGate.Cli.Rendering;
using PowerBIQualityGate.Core.Inspection;
using PowerBIQualityGate.Core.Quality;
using PowerBIQualityGate.Core.Quality.Rules;

namespace PowerBIQualityGate.Tests;

public sealed class DaxRootShapeProfilerTests
{
    [Theory]
    [InlineData(" VAR sales = 1 RETURN sales", DaxRootShapeKind.Variable)]
    [InlineData("CALCULATE(SUM('Sales'[Amount]))", DaxRootShapeKind.Calculate)]
    [InlineData("calculatetable ( FILTER(Data, TRUE()) )", DaxRootShapeKind.Calculate)]
    [InlineData("DIVIDE([Actual], [Target])", DaxRootShapeKind.Divide)]
    [InlineData("IF(TRUE(), 1, 0)", DaxRootShapeKind.Conditional)]
    [InlineData("SWITCH(TRUE(), 1, 2)", DaxRootShapeKind.Conditional)]
    [InlineData("COALESCE([A], [B])", DaxRootShapeKind.Conditional)]
    [InlineData("SUMX(Data, Data[Value])", DaxRootShapeKind.Iterator)]
    [InlineData("rankx ( ALL(Data), [Sales])", DaxRootShapeKind.Iterator)]
    [InlineData("SELECTEDVALUE(Data[Status])", DaxRootShapeKind.OtherFunction)]
    [InlineData("FORMAT([Value], \"0.0\")", DaxRootShapeKind.OtherFunction)]
    [InlineData("[Actual] / [Target]", DaxRootShapeKind.ReferenceOrOperator)]
    [InlineData("1 + 2", DaxRootShapeKind.ReferenceOrOperator)]
    [InlineData("\"COUNTROWS(Data)\"", DaxRootShapeKind.ReferenceOrOperator)]
    [InlineData("-- head comment\nCOUNTROWS(Data)", DaxRootShapeKind.Other)]
    [InlineData("/* comment */ SUM(Data[Value])", DaxRootShapeKind.Other)]
    [InlineData("  ", DaxRootShapeKind.Other)]
    public void ReportsCoarseStartingShapeOnly(string expression, DaxRootShapeKind expected)
    {
        Assert.Equal(expected, DaxRootShapeProfiler.Analyze(expression));
    }

    [Fact]
    public void ProfilesOnlyUnresolvedAndVisibleMeasuresWithoutChangingQualityGate()
    {
        var snapshot = new TomModelSnapshot("PrivateModel", 1600,
        [
            new TomTableSnapshot("SecretTable", false, [],
            [
                New("First", "VAR x = 1 RETURN x"),
                New("Second", "CALCULATE(SUM(SecretTable[Value]))"),
                New("Third", "CALCULATE(SUM(SecretTable[Value]))", "0.00"),
                New("Fourth", "DIVIDE([A], [B])"),
                New("Fifth", "IF(TRUE(), 1, 0)"),
                New("Sixth", "SUM(SecretTable[Value])"),
                New("Known", "VAR x = 1 RETURN x", type: "Double"),
                New("Hidden", "VAR x = 1 RETURN x", hidden: true)
            ], 0, 1)
        ], []);

        var report = new MeasureInspectionEngine().Inspect(snapshot);
        var summary = report.Summary;
        Assert.Equal(7, summary.Visible);
        Assert.Equal(6, summary.UnknownTypes);
        Assert.Equal(1, summary.NumericTypes);
        Assert.Equal(5, summary.StaticDaxUnresolved);
        Assert.Equal(1, summary.StaticDaxLikelyNumeric);
        Assert.Equal(5, summary.UnresolvedRootShapes.Sum(x => x.Count));
        Assert.Equal(4, summary.UnresolvedRootShapes.Sum(x => x.MissingFormat));
        Assert.Contains(summary.UnresolvedRootShapes, x =>
            x.Shape == DaxRootShapeKind.Variable && x.Count == 1 && x.MissingFormat == 1);
        Assert.Contains(summary.UnresolvedRootShapes, x =>
            x.Shape == DaxRootShapeKind.Calculate && x.Count == 2 && x.MissingFormat == 1);
        Assert.Contains(summary.UnresolvedRootShapes, x =>
            x.Shape == DaxRootShapeKind.Divide && x.Count == 1 && x.MissingFormat == 1);
        Assert.Contains(summary.UnresolvedRootShapes, x =>
            x.Shape == DaxRootShapeKind.Conditional && x.Count == 1 && x.MissingFormat == 1);
        Assert.Equal(DaxRootShapeKind.NotApplicable,
            Assert.Single(report.Measures, x => x.MeasureName == "Sixth").UnresolvedRootShape);

        var audit = new ModelQualityAuditEngine(ModelQualityRulePack.CreateDefault()).Run(snapshot);
        var coverage = ModelAuditCoverageAnalyzer.Analyze(snapshot, audit);
        Assert.Equal(1, coverage.MeasureFormatting!.CheckedNumeric);
        Assert.Equal(6, coverage.MeasureFormatting.UnknownType);
        Assert.Single(new UnformattedNumericMeasureRule().Evaluate(snapshot));
    }

    [Fact]
    public void AggregationDoesNotLeakNamesExpressionsOrFormats()
    {
        var snapshot = new TomModelSnapshot("PrivateModel", 1600,
        [new TomTableSnapshot("SecretTable", false, [],
        [
            New("SensitiveKPI", "CALCULATE([SecretRevenue], Private[HighlySensitive])"),
            New("UnknownKPI", "VAR mySecret = 1 RETURN mySecret")
        ], 0, 1)], []);
        var report = new MeasureInspectionEngine().Inspect(snapshot);
        using var writer = new StringWriter();
        new MeasureInspectionRenderer(writer).Render("C:/Secret/Example.pbip", report);
        var output = writer.ToString();
        Assert.Contains("Unresolved DAX entry shapes", output);
        Assert.Contains("CALCULATE / CALCULATETABLE: 1 / 1", output);
        Assert.Contains("VAR / RETURN: 1 / 1", output);
        Assert.DoesNotContain("SensitiveKPI", output);
        Assert.DoesNotContain("UnknownKPI", output);
        Assert.DoesNotContain("SecretRevenue", output);
        Assert.DoesNotContain("HighlySensitive", output);
        Assert.DoesNotContain("mySecret", output);
        Assert.DoesNotContain("SecretTable", output);
        Assert.DoesNotContain('\u001b', output);
    }

    private static TomMeasureSnapshot New(string name, string expression,
        string? format = null, bool hidden = false, string type = "Unknown") =>
        new(name, expression, format, hidden, type);
}

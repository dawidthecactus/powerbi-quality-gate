using PowerBIQualityGate.Cli.Rendering;
using PowerBIQualityGate.Core.Inspection;
using PowerBIQualityGate.Core.Quality;
using PowerBIQualityGate.Core.Quality.Rules;

namespace PowerBIQualityGate.Tests;

public sealed class DaxCalculateScalarEvidenceTests
{
    [Theory]
    [InlineData("VAR x = CALCULATE(SUM(Sales[Amount])) RETURN x")]
    [InlineData("VAR x = CALCULATE(SUM(Sales[Amount]), Sales[Year] = 2026) RETURN x")]
    [InlineData("VAR x = CALCULATE ( COUNTROWS('Sales'), FILTER(Sales, Sales[Year] = 2026)) RETURN x")]
    [InlineData("VAR x = CALCULATE(COUNTROWS('A,B'), 'Year, Name'[Year] = 2026) RETURN x")]
    [InlineData("VAR x = CALCULATE(COUNTROWS('Sales'), 'Sales'[a,b] = 1) RETURN x")]
    [InlineData("VAR x = CALCULATE(COUNTROWS('Sales'), FILTER(Sales, Sales[Label] = \"A,B\")) RETURN x")]
    [InlineData("VAR x = CALCULATE(COUNTROWS('A''B'), 'T'[a]]b] = 1) RETURN x")]
    [InlineData("VAR x = CALCULATE(42) RETURN x")]
    [InlineData("VAR x = CALCULATE(42, {1, 2}) RETURN x")]
    [InlineData("VAR x = CALCULATE(42; Sales[Year] = 2026) RETURN x")]
    [InlineData("VAR a = SUM(Sales[Amount]) RETURN CALCULATE(a, Sales[Year] = 2026)")]
    [InlineData("VAR a = SUM(Sales[Amount]) VAR b = CALCULATE(a, Sales[Year] = 2026) RETURN b")]
    [InlineData("VAR a = CALCULATE(CALCULATE(SUM(Sales[Amount]), Sales[Year] = 2026)) RETURN a")]
    [InlineData("VAR x = CALCULATE(SUM(Sales[Amount]), Sales[Year] = 2026) RETURN CALCULATE(x)")]
    [InlineData("VAR x = CALCULATE(SUM(Sales[Amount]) /*note*/, Sales[Year] = 2026) RETURN x")]
    public void CompleteCalculateWrapperCanPropagateNumericEvidence(string expression) =>
        Assert.Equal(StaticDaxTypeEvidenceKind.LikelyNumeric,
            DaxVarReturnEvidence.Analyze(expression));

    [Theory]
    [InlineData("VAR x = CALCULATE(\"literal\") RETURN x")]
    [InlineData("VAR x = CALCULATE(TRUE(), 'T'[Year] = 2026) RETURN x")]
    [InlineData("VAR x = \"hello\" RETURN CALCULATE(x)")]
    public void ScalarNonNumericEvidenceRemainsNonNumeric(string expression) =>
        Assert.Equal(StaticDaxTypeEvidenceKind.LikelyNonNumeric,
            DaxVarReturnEvidence.Analyze(expression));

    [Theory]
    [InlineData("VAR x = CALCULATETABLE(Sales) RETURN x")]
    [InlineData("VAR x = CALCULATETABLE(COUNTROWS(Sales)) RETURN x")]
    [InlineData("VAR x = CALCULATE_TABLE(SUM(Sales[Amount])) RETURN x")]
    [InlineData("VAR x = CALCULATE(SUM(Sales[Amount])) + 1 RETURN x")]
    [InlineData("VAR x = CALCULATE(SUM(Sales[Amount]),) RETURN x")]
    [InlineData("VAR x = CALCULATE(, Sales[Year] = 1) RETURN x")]
    [InlineData("VAR x = CALCULATE() RETURN x")]
    [InlineData("VAR x = CALCULATE(SUM(Sales[Amount]), Sales[Year] = 2026,) RETURN x")]
    [InlineData("VAR x = CALCULATE(SUM(Sales[Amount]), Sales[Year] = 2026 RETURN x")]
    [InlineData("VAR x = CALCULATE(SUM(Sales[Amount]) RETURN x")]
    [InlineData("VAR x = CALCULATE(SUM(Sales[Amount]) )) RETURN x")]
    [InlineData("VAR x = CALCULATE(SUM(Sales[Amount]) ) ELSE 1 RETURN x")]
    [InlineData("VAR x = CALCULATE([Other], 'T'[Year] = 2026) RETURN x")]
    [InlineData("VAR x = CALCULATE(DIVIDE(1, 2)) RETURN x")]
    [InlineData("VAR x = CALCULATE(CALCULATETABLE(Sales)) RETURN x")]
    [InlineData("VAR x = CALCULATE(IF(TRUE(), 1, \"no\")) RETURN x")]
    [InlineData("VAR x = CALCULATE(y) VAR y = 2 RETURN x")]
    [InlineData("VAR x = CALCULATE(x) RETURN x")]
    [InlineData("VAR x = CALCULATE(SUM(Sales[Amount]), \"unfinished) RETURN x")]
    [InlineData("VAR x = CALCULATE(SUM(Sales[Amount]), 'T'[Unclosed) RETURN x")]
    [InlineData("VAR x = CALCULATE(SUM(Sales[Amount]), {1, 2) RETURN x")]
    [InlineData("VAR x = CALCULATE(SUM(Sales[Amount]), /* unclosed) RETURN x")]
    public void UnsupportedOrMalformedWrappersDoNotProduceTypeEvidence(string expression) =>
        Assert.Equal(StaticDaxTypeEvidenceKind.Unresolved,
            DaxVarReturnEvidence.Analyze(expression));

    [Fact]
    public void UnresolvedCalculatePreservesPreviousReasonAndBlockedShape()
    {
        var result = DaxVarReturnEvidence.AnalyzeDetailed(
            "VAR x = CALCULATE([PrivateMeasure], 'T'[Year] = 2026) RETURN x");
        Assert.Equal(StaticDaxTypeEvidenceKind.Unresolved, result.Evidence);
        Assert.Equal(DaxVarReturnDiagnosticReason.UnsupportedVariableExpression, result.Reason);
        Assert.Equal(DaxRootShapeKind.Calculate, result.BlockedRootShape);
    }

    [Fact]
    public void IntegrationOnlyAddsAdvisoryVarCountsAndPreservesSm002Warnings()
    {
        var snapshot = new TomModelSnapshot("Local", 1600,
        [new TomTableSnapshot("Secret", false, [],
        [
            Measure("WrappedNumber", "VAR x = CALCULATE(SUM(Secret[Value]), Secret[Year] = 2026) RETURN x"),
            Measure("WrappedText", "VAR x = CALCULATE(\"private\", Secret[Year] = 2026) RETURN x"),
            Measure("Nested", "VAR x = CALCULATE(CALCULATE(COUNTROWS('Secret'))) RETURN x", "#,##0"),
            Measure("Unresolved", "VAR x = CALCULATE([SensitiveMeasure], Secret[Year] = 2026) RETURN x"),
            Measure("TableShape", "VAR t = CALCULATETABLE('Secret') RETURN t"),
            Measure("NonVar", "CALCULATE(SUM(Secret[Value]))"),
            Measure("Known", "VAR x = CALCULATE(SUM(Secret[Value])) RETURN x", type: "Int64"),
            Measure("Hidden", "VAR x = CALCULATE(SUM(Secret[Value])) RETURN x", hidden: true)
        ], 0, 1)], []);

        var report = new MeasureInspectionEngine().Inspect(snapshot);
        var summary = report.Summary;
        Assert.Equal(7, summary.Visible);
        Assert.Equal(6, summary.UnknownTypes);
        Assert.Equal(5, summary.VarReturnAnalyzed);
        Assert.Equal(2, summary.VarReturnLikelyNumeric);
        Assert.Equal(1, summary.VarReturnLikelyNonNumeric);
        Assert.Equal(2, summary.VarReturnUnresolved);
        Assert.Equal(1, summary.VarReturnLikelyNumericMissingFormat);
        Assert.Equal(6, summary.StaticDaxUnresolved);
        Assert.Equal(5, Assert.Single(summary.UnresolvedRootShapes,
            x => x.Shape == DaxRootShapeKind.Variable).Count);
        Assert.Equal(2, summary.VarReturnUnresolvedReasons.Sum(r => r.Count));
        Assert.Equal(2, summary.VarReturnBlockedShapes.Sum(r => r.Count));

        Assert.Single(new UnformattedNumericMeasureRule().Evaluate(snapshot));
        var audit = new ModelQualityAuditEngine(ModelQualityRulePack.CreateDefault()).Run(snapshot);
        var coverage = ModelAuditCoverageAnalyzer.Analyze(snapshot, audit);
        Assert.Equal(1, coverage.MeasureFormatting!.CheckedNumeric);
        Assert.Equal(6, coverage.MeasureFormatting.UnknownType);
        Assert.Equal(1, coverage.Sm002Assessment!.ConfirmedWarnings);
    }

    [Fact]
    public void TerminalOutputDoesNotLeakPrivateDaxOrMeasureIdentifiers()
    {
        var snapshot = new TomModelSnapshot("Private", 1600,
        [new TomTableSnapshot("Secret", false, [],
        [
            Measure("SecretNumber", "VAR privateNumber = CALCULATE(SUM(Secret[HighlySensitive])) RETURN privateNumber"),
            Measure("SecretText", "VAR confidential = CALCULATE(\"SecretContent\") RETURN confidential")
        ], 0, 1)], []);
        using var writer = new StringWriter();
        new MeasureInspectionRenderer(writer).Render("C:/Private/Example.pbip",
            new MeasureInspectionEngine().Inspect(snapshot));
        var output = writer.ToString();
        Assert.Contains("VAR / RETURN evidence", output);
        Assert.Contains("likely numeric 1", output);
        Assert.Contains("likely nonnumeric 1", output);
        Assert.DoesNotContain("HighlySensitive", output);
        Assert.DoesNotContain("SecretNumber", output);
        Assert.DoesNotContain("SecretContent", output);
        Assert.DoesNotContain("privateNumber", output);
        Assert.DoesNotContain("confidential", output);
        Assert.DoesNotContain('\u001b', output);
    }

    private static TomMeasureSnapshot Measure(string name, string expression,
        string? format = null, bool hidden = false, string type = "Unknown") =>
        new(name, expression, format, hidden, type);
}

using PowerBIQualityGate.Cli.Rendering;
using PowerBIQualityGate.Core.Inspection;
using PowerBIQualityGate.Core.Quality;
using PowerBIQualityGate.Core.Quality.Rules;

namespace PowerBIQualityGate.Tests;

public sealed class DaxVarReturnEvidenceTests
{
    [Theory]
    [InlineData("VAR total = COUNTROWS('Sales') RETURN total")]
    [InlineData(" VAR __amount = SUM('Sales'[Amount]) RETURN __amount ")]
    [InlineData("var value = 42 return VALUE")]
    [InlineData("VAR x = -12.50 RETURN x")]
    [InlineData("VAR first = 1 VAR second = first RETURN second")]
    [InlineData("VAR first = 1 VAR second = first VAR third = second RETURN third")]
    [InlineData("VAR x = 42 RETURN COUNTROWS('Sales')")]
    [InlineData("VAR x = \"RETURN VAR\" RETURN COUNTROWS('Sales')")]
    [InlineData("VAR x = COUNTROWS(FILTER('VAR RETURN', 'VAR RETURN'[Amount] > 0)) RETURN x")]
    [InlineData("VAR x = 12 // RETURN decoy\nRETURN x")]
    [InlineData("VAR x = 12 /* VAR decoy RETURN decoy */ RETURN x")]
    [InlineData("VAR x = COUNTROWS('A''B') RETURN x")]
    [InlineData("VAR x = COUNTROWS('Data'[Right]]Now]) RETURN x")]
    [InlineData("VAR x = SUM('Sales'[Amount]) RETURN /* note */ x")]
    public void SafeScalarVariableShapesProduceAdvisoryNumericEvidence(string expression)
    {
        Assert.Equal(StaticDaxTypeEvidenceKind.LikelyNumeric,
            DaxVarReturnEvidence.Analyze(expression));
    }

    [Theory]
    [InlineData("VAR label = \"COUNTROWS('Sales')\" RETURN label")]
    [InlineData("VAR label = \"hello \"\"RETURN\"\"\" RETURN label")]
    [InlineData("VAR flag = TRUE() RETURN flag")]
    [InlineData("VAR label = \"hello\" RETURN label")]
    [InlineData("VAR x = 1 RETURN \"literal\"")]
    [InlineData("VAR a = \"text\" VAR b = a RETURN b")]
    public void TextAndBooleanReturnEvidenceIsNotNumeric(string expression)
    {
        Assert.Equal(StaticDaxTypeEvidenceKind.LikelyNonNumeric,
            DaxVarReturnEvidence.Analyze(expression));
    }

    [Theory]
    [InlineData("VAR value = [Measure] RETURN value")]
    [InlineData("VAR value = DIVIDE(1, 2) RETURN value")]
    [InlineData("VAR value = CALCULATE(SUM(Data[Value])) RETURN value")]
    [InlineData("VAR value = 1 RETURN IF(TRUE(), value, 0)")]
    [InlineData("VAR value = 1 RETURN value + 1")]
    [InlineData("VAR value = 1 RETURN (value)")]
    [InlineData("VAR value = 1 RETURN other")]
    [InlineData("VAR first = second VAR second = 1 RETURN first")]
    [InlineData("VAR x = x RETURN x")]
    [InlineData("VAR first = second VAR second = first RETURN second")]
    [InlineData("VAR value = 1 VAR value = 2 RETURN value")]
    [InlineData("VAR value = 1 RETURN value RETURN value")]
    [InlineData("VAR value = 1 RETURN value VAR another = 2")]
    [InlineData("VAR value = 1 RETURN")]
    [InlineData("VAR value = RETURN 1")]
    [InlineData("VAR value == 1 RETURN value")]
    [InlineData("VAR = 1 RETURN 42")]
    [InlineData("RETURN 1")]
    [InlineData("VAR value = 1")]
    [InlineData("VAR _value = 1 RETURN _value")]
    [InlineData("VAR value_one = 1 RETURN value_one")]
    [InlineData("VAR 1value = 1 RETURN 1value")]
    [InlineData("VAR x = 1 RETURN x /* unclosed")]
    [InlineData("VAR x = \"unterminated RETURN x")]
    [InlineData("VAR x = COUNTROWS(Data[Unclosed) RETURN x")]
    [InlineData("VAR x = COUNTROWS(Data RETURN x")]
    [InlineData("VAR x = 1 RETURN x) ")]
    [InlineData("VAR x = 1 RETURN x + SUM(Data[Value])")]
    [InlineData("VAR x = 1RETURN x")]
    [InlineData("VAR x = SUM(Data[Value])RETURN x")]
    [InlineData("\"VAR x = 1 RETURN x\"")]
    [InlineData("VAR x = SUM(Data[Value]) + 1 RETURN x")]
    public void AmbiguousInvalidOrUnsupportedShapesRemainUnresolved(string expression)
    {
        Assert.Equal(StaticDaxTypeEvidenceKind.Unresolved,
            DaxVarReturnEvidence.Analyze(expression));
    }

    [Fact]
    public void NullEmptyAndOversizedExpressionsRemainUnresolved()
    {
        Assert.Equal(StaticDaxTypeEvidenceKind.Unresolved, DaxVarReturnEvidence.Analyze(null));
        Assert.Equal(StaticDaxTypeEvidenceKind.Unresolved, DaxVarReturnEvidence.Analyze("   "));
        Assert.Equal(StaticDaxTypeEvidenceKind.Unresolved,
            DaxVarReturnEvidence.Analyze("VAR x = " + new string('a', 16400) + " RETURN x"));
    }

    [Fact]
    public void AnalysisRemainsIndependentOfExistingStaticEvidenceAndQualityGate()
    {
        var snapshot = new TomModelSnapshot("Synthetic", 1600,
        [new TomTableSnapshot("Secret", false, [],
        [
            Measure("NumericVar", "VAR total = COUNTROWS('Secret') RETURN total"),
            Measure("TextVar", "VAR label = \"secret\" RETURN label"),
            Measure("UnknownVar", "VAR result = [Sensitive] RETURN result"),
            Measure("FormattedVar", "VAR number = 42 RETURN number", "0.00"),
            Measure("Standalone", "COUNTROWS(Secret)"),
            Measure("Known", "VAR number = 42 RETURN number", type: "Int64"),
            Measure("Hidden", "VAR number = 42 RETURN number", hidden: true)
        ], 0, 1)], []);

        var report = new MeasureInspectionEngine().Inspect(snapshot);
        var summary = report.Summary;
        Assert.Equal(6, summary.Visible);
        Assert.Equal(5, summary.UnknownTypes);
        Assert.Equal(4, summary.StaticDaxUnresolved);
        Assert.Equal(1, summary.StaticDaxLikelyNumeric);
        Assert.Equal(4, summary.VarReturnAnalyzed);
        Assert.Equal(2, summary.VarReturnLikelyNumeric);
        Assert.Equal(1, summary.VarReturnLikelyNonNumeric);
        Assert.Equal(1, summary.VarReturnUnresolved);
        Assert.Equal(1, summary.VarReturnLikelyNumericMissingFormat);
        Assert.Equal(4, Assert.Single(summary.UnresolvedRootShapes,
            x => x.Shape == DaxRootShapeKind.Variable).Count);
        Assert.Equal(StaticDaxTypeEvidenceKind.NotAssessed,
            Assert.Single(report.Measures, x => x.MeasureName == "Known").VarReturnEvidence);
        Assert.Equal(StaticDaxTypeEvidenceKind.NotAssessed,
            Assert.Single(report.Measures, x => x.MeasureName == "Standalone").VarReturnEvidence);

        var warning = Assert.Single(new UnformattedNumericMeasureRule().Evaluate(snapshot));
        Assert.Contains("Known", warning.Location);
        var audit = new ModelQualityAuditEngine(ModelQualityRulePack.CreateDefault()).Run(snapshot);
        var coverage = ModelAuditCoverageAnalyzer.Analyze(snapshot, audit);
        Assert.Equal(1, coverage.MeasureFormatting!.CheckedNumeric);
        Assert.Equal(5, coverage.MeasureFormatting.UnknownType);
        Assert.Equal(1, coverage.Sm002Assessment!.ConfirmedWarnings);
    }

    [Fact]
    public void RenderedVarEvidenceContainsOnlyFixedAggregates()
    {
        var snapshot = new TomModelSnapshot("PrivateModel", 1600,
        [new TomTableSnapshot("SecretTable", false, [],
        [
            Measure("SecretRevenue", "VAR VerySensitive = COUNTROWS('SecretTable') RETURN VerySensitive"),
            Measure("SecretText", "VAR PrivateLabel = \"PrivateCode\" RETURN PrivateLabel"),
            Measure("SecretUnknown", "VAR Confidential = [SensitiveMetric] RETURN Confidential")
        ], 0, 1)], []);
        using var writer = new StringWriter();
        new MeasureInspectionRenderer(writer).Render("C:/Private/Example.pbip",
            new MeasureInspectionEngine().Inspect(snapshot));
        var output = writer.ToString();

        Assert.Contains("VAR / RETURN evidence (unresolved TOM/DAX)", output);
        Assert.Contains("likely numeric 1", output);
        Assert.Contains("likely nonnumeric 1", output);
        Assert.Contains("unresolved 1", output);
        Assert.Contains("VAR / RETURN review: missing format among likely numeric 1", output);
        Assert.DoesNotContain("SecretTable", output);
        Assert.DoesNotContain("SecretRevenue", output);
        Assert.DoesNotContain("VerySensitive", output);
        Assert.DoesNotContain("PrivateLabel", output);
        Assert.DoesNotContain("PrivateCode", output);
        Assert.DoesNotContain("SensitiveMetric", output);
        Assert.DoesNotContain("COUNTROWS(", output);
        Assert.DoesNotContain('\u001b', output);
    }

    [Fact]
    public void UnresolvedVarStringsDoNotChangeLegacyRootCounters()
    {
        Assert.Equal(StaticDaxTypeEvidenceKind.Unresolved,
            StaticDaxTypeEvidence.Analyze("VAR sales = 1 RETURN sales"));
        Assert.Equal(StaticDaxTypeEvidenceKind.LikelyNumeric,
            DaxVarReturnEvidence.Analyze("VAR sales = 1 RETURN sales"));
    }

    private static TomMeasureSnapshot Measure(string name, string expression,
        string? format = null, bool hidden = false, string type = "Unknown") =>
        new(name, expression, format, hidden, type);
}

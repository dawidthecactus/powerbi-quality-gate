using PowerBIQualityGate.Cli.Rendering;
using PowerBIQualityGate.Core.Inspection;
using PowerBIQualityGate.Core.Quality;
using PowerBIQualityGate.Core.Quality.Rules;

namespace PowerBIQualityGate.Tests;

public sealed class DaxVarReturnDiagnosticsTests
{
    [Theory]
    [InlineData("VAR x = DIVIDE(1, 2) RETURN x", DaxVarReturnDiagnosticReason.UnsupportedVariableExpression, DaxRootShapeKind.Divide)]
    [InlineData("VAR x = IF(TRUE(), 1, \"no\") RETURN x", DaxVarReturnDiagnosticReason.UnsupportedVariableExpression, DaxRootShapeKind.Conditional)]
    [InlineData("VAR x = FORMAT(42, \"0\") RETURN x", DaxVarReturnDiagnosticReason.UnsupportedVariableExpression, DaxRootShapeKind.OtherFunction)]
    [InlineData("VAR x = [Sensitive] RETURN x", DaxVarReturnDiagnosticReason.UnsupportedVariableExpression, DaxRootShapeKind.ReferenceOrOperator)]
    [InlineData("VAR x = (42) RETURN x", DaxVarReturnDiagnosticReason.UnsupportedVariableExpression, DaxRootShapeKind.ReferenceOrOperator)]
    [InlineData("VAR x = 1 RETURN DIVIDE(1, 2)", DaxVarReturnDiagnosticReason.UnsupportedReturnExpression, DaxRootShapeKind.Divide)]
    [InlineData("VAR x = 1 RETURN IF(TRUE(), 1, \"no\")", DaxVarReturnDiagnosticReason.UnsupportedReturnExpression, DaxRootShapeKind.Conditional)]
    [InlineData("VAR x = 1 RETURN x + 1", DaxVarReturnDiagnosticReason.UnsupportedReturnExpression, DaxRootShapeKind.ReferenceOrOperator)]
    [InlineData("VAR x = 1 RETURN [AnotherMeasure]", DaxVarReturnDiagnosticReason.UnsupportedReturnExpression, DaxRootShapeKind.ReferenceOrOperator)]
    [InlineData("VAR x = 1 RETURN y", DaxVarReturnDiagnosticReason.UnknownOrForwardVariable, DaxRootShapeKind.NotApplicable)]
    [InlineData("VAR x = y VAR y = 1 RETURN x", DaxVarReturnDiagnosticReason.UnknownOrForwardVariable, DaxRootShapeKind.NotApplicable)]
    [InlineData("VAR x = x RETURN x", DaxVarReturnDiagnosticReason.UnknownOrForwardVariable, DaxRootShapeKind.NotApplicable)]
    [InlineData("VAR x = 1", DaxVarReturnDiagnosticReason.StructureRejected, DaxRootShapeKind.NotApplicable)]
    [InlineData("VAR x = 1 RETURN", DaxVarReturnDiagnosticReason.StructureRejected, DaxRootShapeKind.NotApplicable)]
    [InlineData("VAR x = 1 RETURN x RETURN x", DaxVarReturnDiagnosticReason.StructureRejected, DaxRootShapeKind.NotApplicable)]
    [InlineData("VAR _x = 1 RETURN _x", DaxVarReturnDiagnosticReason.StructureRejected, DaxRootShapeKind.NotApplicable)]
    [InlineData("VAR x = 1RETURN x", DaxVarReturnDiagnosticReason.StructureRejected, DaxRootShapeKind.NotApplicable)]
    [InlineData("VAR x = \"unclosed RETURN x", DaxVarReturnDiagnosticReason.StructureRejected, DaxRootShapeKind.NotApplicable)]
    [InlineData("VAR x = 1 RETURN x /* unclosed", DaxVarReturnDiagnosticReason.StructureRejected, DaxRootShapeKind.NotApplicable)]
    public void ReturnsFixedReasonForEachUnresolvedShape(string dax,
        DaxVarReturnDiagnosticReason reason, DaxRootShapeKind blockedShape)
    {
        var result = DaxVarReturnEvidence.AnalyzeDetailed(dax);
        Assert.Equal(StaticDaxTypeEvidenceKind.Unresolved, result.Evidence);
        Assert.Equal(reason, result.Reason);
        Assert.Equal(blockedShape, result.BlockedRootShape);
        Assert.Equal(DaxVarReturnEvidence.Analyze(dax), result.Evidence);
    }

    [Theory]
    [InlineData("VAR x = 42 RETURN x", StaticDaxTypeEvidenceKind.LikelyNumeric)]
    [InlineData("VAR x = COUNTROWS('Data') RETURN x", StaticDaxTypeEvidenceKind.LikelyNumeric)]
    [InlineData("VAR x = CALCULATE(SUM(Data[Amount])) RETURN x", StaticDaxTypeEvidenceKind.LikelyNumeric)]
    [InlineData("VAR x = IF(TRUE(), 1, 0) RETURN x", StaticDaxTypeEvidenceKind.LikelyNumeric)]
    [InlineData("VAR first = 1 VAR second = first RETURN second", StaticDaxTypeEvidenceKind.LikelyNumeric)]
    [InlineData("VAR x = \"COUNTROWS(Sales)\" RETURN x", StaticDaxTypeEvidenceKind.LikelyNonNumeric)]
    [InlineData("VAR x = FALSE() RETURN x", StaticDaxTypeEvidenceKind.LikelyNonNumeric)]
    [InlineData("VAR x = 1 RETURN \"text\"", StaticDaxTypeEvidenceKind.LikelyNonNumeric)]
    [InlineData("VAR x = 1 // RETURN fake\nRETURN x", StaticDaxTypeEvidenceKind.LikelyNumeric)]
    [InlineData("VAR x = COUNTROWS('A''B') RETURN x", StaticDaxTypeEvidenceKind.LikelyNumeric)]
    public void SuccessfulLegacyEvidenceKeepsNoFailureReason(string dax,
        StaticDaxTypeEvidenceKind expected)
    {
        var result = DaxVarReturnEvidence.AnalyzeDetailed(dax);
        Assert.Equal(expected, result.Evidence);
        Assert.Equal(DaxVarReturnDiagnosticReason.NotApplicable, result.Reason);
        Assert.Equal(DaxRootShapeKind.NotApplicable, result.BlockedRootShape);
        Assert.Equal(DaxVarReturnEvidence.Analyze(dax), result.Evidence);
    }

    [Fact]
    public void LimitsAreReportedWithoutInferringType()
    {
        var tooLong = "VAR x = " + new string('a', 17000) + " RETURN x";
        var tooMany = string.Join(' ', Enumerable.Range(0, 65).Select(i => $"VAR v{i} = {i}")) + " RETURN v64";
        var deep = "VAR v0 = 1 " +
            string.Concat(Enumerable.Range(1, 35).Select(i => $"VAR v{i} = v{i - 1} ")) + "RETURN v34";
        foreach (var dax in new[] { tooLong, tooMany, deep })
        {
            var result = DaxVarReturnEvidence.AnalyzeDetailed(dax);
            Assert.Equal(StaticDaxTypeEvidenceKind.Unresolved, result.Evidence);
            Assert.Equal(DaxVarReturnDiagnosticReason.AnalysisLimitExceeded, result.Reason);
            Assert.Equal(DaxRootShapeKind.NotApplicable, result.BlockedRootShape);
        }
    }

    [Fact]
    public void AggregateReasonsPartitionOnlyVisibleUnresolvedVarsAndPreserveGate()
    {
        var snapshot = Model(
            Measure("SecretDivide", "VAR x = DIVIDE(10, 2) RETURN x"),
            Measure("SecretReturn", "VAR x = 1 RETURN IF(TRUE(), 1, [Unknown])"),
            Measure("SecretUnknown", "VAR x = 1 RETURN missing", "0.00"),
            Measure("SecretStructure", "VAR _x = 1 RETURN _x"),
            Measure("SecretGood", "VAR x = COUNTROWS('Data') RETURN x"),
            Measure("KnownNumeric", "VAR x = 1 RETURN x", type: "Int64"),
            Measure("Hidden", "VAR x = DIVIDE(1, 2) RETURN x", hidden: true),
            Measure("Other", "COUNTROWS('Data')"));
        var inspected = new MeasureInspectionEngine().Inspect(snapshot);
        var s = inspected.Summary;
        Assert.Equal(7, s.Visible);
        Assert.Equal(5, s.VarReturnAnalyzed);
        Assert.Equal(1, s.VarReturnLikelyNumeric);
        Assert.Equal(4, s.VarReturnUnresolved);
        Assert.Equal(4, s.VarReturnUnresolvedReasons.Sum(x => x.Count));
        Assert.Equal(3, s.VarReturnUnresolvedReasons.Sum(x => x.MissingFormat));
        Assert.Equal(2, s.VarReturnBlockedShapes.Sum(x => x.Count));
        Assert.Equal(2, s.VarReturnBlockedShapes.Sum(x => x.MissingFormat));
        Assert.Contains(s.VarReturnUnresolvedReasons, x =>
            x.Reason == DaxVarReturnDiagnosticReason.StructureRejected && x.Count == 1);
        Assert.Contains(s.VarReturnUnresolvedReasons, x =>
            x.Reason == DaxVarReturnDiagnosticReason.UnsupportedVariableExpression && x.Count == 1);
        Assert.Contains(s.VarReturnUnresolvedReasons, x =>
            x.Reason == DaxVarReturnDiagnosticReason.UnsupportedReturnExpression && x.Count == 1);
        Assert.Contains(s.VarReturnUnresolvedReasons, x =>
            x.Reason == DaxVarReturnDiagnosticReason.UnknownOrForwardVariable && x.Count == 1 && x.MissingFormat == 0);
        Assert.Equal(DaxVarReturnDiagnosticReason.NotApplicable,
            Assert.Single(inspected.Measures, x => x.MeasureName == "Hidden").VarReturnReason);
        Assert.Equal(DaxVarReturnDiagnosticReason.NotApplicable,
            Assert.Single(inspected.Measures, x => x.MeasureName == "KnownNumeric").VarReturnReason);

        Assert.Single(new UnformattedNumericMeasureRule().Evaluate(snapshot));
        var audit = new ModelQualityAuditEngine(ModelQualityRulePack.CreateDefault()).Run(snapshot);
        var coverage = ModelAuditCoverageAnalyzer.Analyze(snapshot, audit);
        Assert.Equal(1, coverage.MeasureFormatting!.CheckedNumeric);
        Assert.Equal(6, coverage.MeasureFormatting.UnknownType);
        Assert.Equal(1, coverage.Sm002Assessment!.ConfirmedWarnings);
    }

    [Fact]
    public void RendererContainsOnlyFixedLabelsAndNumbers()
    {
        var snapshot = Model(
            Measure("ConfidentialDivide", "VAR Secret = DIVIDE([PrivateA], [PrivateB]) RETURN Secret"),
            Measure("ConfidentialReturn", "VAR Secret = 42 RETURN IF(TRUE(), Secret, [Unknown])"),
            Measure("ConfidentialAlias", "VAR HiddenAmount = 1 RETURN NotDeclared"));
        var report = new MeasureInspectionEngine().Inspect(snapshot);
        using var writer = new StringWriter();
        new MeasureInspectionRenderer(writer).Render("C:/Private/Example.pbip", report);
        var output = writer.ToString();
        Assert.Contains("VAR / RETURN unresolved reasons", output);
        Assert.Contains("Unsupported variable expression: 1 / 1", output);
        Assert.Contains("Unsupported RETURN expression: 1 / 1", output);
        Assert.Contains("Unknown / forward alias: 1 / 1", output);
        Assert.Contains("VAR / RETURN blocked expression shapes", output);
        Assert.Contains("DIVIDE: 1 / 1", output);
        Assert.Contains("IF / SWITCH / COALESCE: 1 / 1", output);
        Assert.DoesNotContain("Confidential", output);
        Assert.DoesNotContain("Secret", output);
        Assert.DoesNotContain("PrivateA", output);
        Assert.DoesNotContain("PrivateB", output);
        Assert.DoesNotContain("HiddenAmount", output);
        Assert.DoesNotContain("NotDeclared", output);
        Assert.DoesNotContain("[", output);
        Assert.DoesNotContain('\u001b', output);
    }

    private static TomModelSnapshot Model(params TomMeasureSnapshot[] measures) => new(
        "SyntheticModel", 1600,
        [new TomTableSnapshot("PrivateTable", false, [], measures, 0, 1)], []);

    private static TomMeasureSnapshot Measure(string name, string dax,
        string? format = null, bool hidden = false, string type = "Unknown") =>
        new(name, dax, format, hidden, type);
}

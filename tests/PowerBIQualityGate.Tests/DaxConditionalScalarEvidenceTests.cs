using PowerBIQualityGate.Cli.Rendering;
using PowerBIQualityGate.Core.Inspection;
using PowerBIQualityGate.Core.Quality;
using PowerBIQualityGate.Core.Quality.Rules;

namespace PowerBIQualityGate.Tests;

public sealed class DaxConditionalScalarEvidenceTests
{
    [Theory]
    [InlineData("VAR r = IF(TRUE(), 1, 0) RETURN r")]
    [InlineData("VAR r = IF([Flag], SUM('Sales'[Amount]), 0) RETURN r")]
    [InlineData("VAR r = IF(TRUE(), 42) RETURN r")]
    [InlineData("VAR r = IF(TRUE(), BLANK(), 42) RETURN r")]
    [InlineData("VAR r = IF(TRUE(), BLANK (), SUM(T[Value])) RETURN r")]
    [InlineData("VAR r = SWITCH(TRUE(), TRUE(), 1, FALSE(), 2, 0) RETURN r")]
    [InlineData("VAR r = SWITCH(TRUE(), 1, COUNTROWS('Orders'), 2, SUM(T[Value])) RETURN r")]
    [InlineData("VAR r = SWITCH(TRUE(), TRUE(), 1, 2) RETURN r")]
    [InlineData("VAR r = SWITCH(TRUE(), TRUE(), BLANK(), FALSE(), 1) RETURN r")]
    [InlineData("VAR r = COALESCE(SUM(T[Value]), 0) RETURN r")]
    [InlineData("VAR r = COALESCE(BLANK(), SUM(T[Value]), 0) RETURN r")]
    [InlineData("VAR r = IF(TRUE(), CALCULATE(SUM(T[Value]), T[Year] = 2026), 0) RETURN r")]
    [InlineData("VAR base = 3 VAR r = IF(TRUE(), base, 2) RETURN r")]
    [InlineData("VAR base = SUM(T[Value]) RETURN IF([Flag], base, 0)")]
    [InlineData("VAR r = IF(TRUE(), 1, IF(FALSE(), 2, 3)) RETURN r")]
    [InlineData("VAR r = CALCULATE(IF(TRUE(), 1, 2), T[Year] = 2026) RETURN r")]
    [InlineData("VAR r = IF(TRUE(), COUNTROWS('A''B'), 0) RETURN r")]
    [InlineData("VAR r = SWITCH(TRUE(), TRUE(), SUM('T'[a]]b]), 0) RETURN r")]
    [InlineData("VAR r = IF(TRUE(), 1 /* comment */, 0) RETURN r")]
    [InlineData("VAR r = IF(TRUE(); 1; 2) RETURN r")]
    [InlineData("VAR r = COALESCE(COUNTROWS(FILTER('Sales, V', 'Sales, V'[Year] = 2026)), 0) RETURN r")]
    public void UniformNumericBranchesProduceAdvisoryNumericEvidence(string expression) =>
        Assert.Equal(StaticDaxTypeEvidenceKind.LikelyNumeric,
            DaxVarReturnEvidence.Analyze(expression));

    [Theory]
    [InlineData("VAR r = IF(TRUE(), \"yes\", \"no\") RETURN r")]
    [InlineData("VAR r = IF(TRUE(), \"hello\") RETURN r")]
    [InlineData("VAR r = IF(TRUE(), TRUE(), FALSE()) RETURN r")]
    [InlineData("VAR r = IF(TRUE(), \"yes\", BLANK()) RETURN r")]
    [InlineData("VAR r = SWITCH(TRUE(), TRUE(), \"up\", FALSE(), \"down\", \"other\") RETURN r")]
    [InlineData("VAR r = COALESCE(\"hello\", \"world\") RETURN r")]
    [InlineData("VAR r = COALESCE(BLANK(), \"text\") RETURN r")]
    [InlineData("VAR r = IF(TRUE(), IF(FALSE(), \"a\", \"b\"), \"c\") RETURN r")]
    [InlineData("VAR r = CALCULATE(IF(TRUE(), \"text\", \"other\")) RETURN r")]
    public void UniformNonNumericBranchesProduceNonNumericHint(string expression) =>
        Assert.Equal(StaticDaxTypeEvidenceKind.LikelyNonNumeric,
            DaxVarReturnEvidence.Analyze(expression));

    [Theory]
    [InlineData("VAR r = IF(TRUE(), 1, \"missing\") RETURN r")]
    [InlineData("VAR r = IF(TRUE(), TRUE(), 0) RETURN r")]
    [InlineData("VAR r = IF(TRUE(), 1, [Unknown]) RETURN r")]
    [InlineData("VAR r = IF(TRUE(), [Unknown], 0) RETURN r")]
    [InlineData("VAR r = IF(TRUE(), BLANK(), BLANK()) RETURN r")]
    [InlineData("VAR r = SWITCH(TRUE(), 1, 1, 2, \"x\") RETURN r")]
    [InlineData("VAR r = SWITCH(TRUE(), 1, [Unknown], 2, 0) RETURN r")]
    [InlineData("VAR r = COALESCE(1, \"x\") RETURN r")]
    [InlineData("VAR r = COALESCE([Unknown], 0) RETURN r")]
    [InlineData("VAR r = COALESCE(BLANK(), BLANK()) RETURN r")]
    [InlineData("VAR r = COALESCE(BLANK()) RETURN r")]
    [InlineData("VAR r = IF(TRUE(), 1, ) RETURN r")]
    [InlineData("VAR r = IF(TRUE(), , 1) RETURN r")]
    [InlineData("VAR r = IF(TRUE()) RETURN r")]
    [InlineData("VAR r = IF(TRUE(), 1, 0, 3) RETURN r")]
    [InlineData("VAR r = SWITCH(TRUE(), 1) RETURN r")]
    [InlineData("VAR r = SWITCH(TRUE(), 1, 2,) RETURN r")]
    [InlineData("VAR r = COALESCE(1,) RETURN r")]
    [InlineData("VAR r = IF(TRUE(), 1, 0) + 1 RETURN r")]
    [InlineData("VAR r = IFERROR(1, 0) RETURN r")]
    [InlineData("VAR r = IF.EAGER(TRUE(), 1, 0) RETURN r")]
    [InlineData("VAR r = SWITCH(TRUE(), TRUE(), CALCULATETABLE(T), 0) RETURN r")]
    [InlineData("VAR x = 1 RETURN IF([Flag], x, [Other])")]
    [InlineData("VAR x = IF(TRUE(), x, 1) RETURN x")]
    [InlineData("VAR r = IF(TRUE(), 1, \"unclosed) RETURN r")]
    [InlineData("VAR r = IF(TRUE(), 1, 0] RETURN r")]
    [InlineData("VAR r = IF(TRUE(), 1, 0) -- trailing comment\n RETURN r + 1")]
    public void UnknownMixedOrUnsupportedBranchesStayUnresolved(string expression) =>
        Assert.Equal(StaticDaxTypeEvidenceKind.Unresolved,
            DaxVarReturnEvidence.Analyze(expression));

    [Fact]
    public void ConditionalAnalyzerKeepsReasonsAndBlockedShapesOnUnknownBranches()
    {
        var value = DaxVarReturnEvidence.AnalyzeDetailed(
            "VAR x = IF([Flag], 1, [Sensitive]) RETURN x");
        Assert.Equal(StaticDaxTypeEvidenceKind.Unresolved, value.Evidence);
        Assert.Equal(DaxVarReturnDiagnosticReason.UnsupportedVariableExpression, value.Reason);
        Assert.Equal(DaxRootShapeKind.Conditional, value.BlockedRootShape);

        var returned = DaxVarReturnEvidence.AnalyzeDetailed(
            "VAR x = 1 RETURN SWITCH(TRUE(), TRUE(), x, FALSE(), [Sensitive])");
        Assert.Equal(StaticDaxTypeEvidenceKind.Unresolved, returned.Evidence);
        Assert.Equal(DaxVarReturnDiagnosticReason.UnsupportedReturnExpression, returned.Reason);
        Assert.Equal(DaxRootShapeKind.Conditional, returned.BlockedRootShape);
    }

    [Fact]
    public void IntegrationChangesOnlyAdvisoryAggregatesAndPreservesSm002Gate()
    {
        var snapshot = new TomModelSnapshot("SecretModel", 1600,
        [new TomTableSnapshot("SecretTable", false, [],
        [
            New("NumericIf", "VAR v = IF([Condition], 1, 2) RETURN v"),
            New("NumericSwitch", "VAR v = SWITCH(TRUE(), TRUE(), SUM('SecretTable'[Value]), 0) RETURN v"),
            New("TextCoalesce", "VAR v = COALESCE(\"secret\", \"private\") RETURN v"),
            New("UnknownIf", "VAR v = IF(TRUE(), [Sensitive], 0) RETURN v"),
            New("MixedIf", "VAR v = IF(TRUE(), 1, \"x\") RETURN v"),
            New("Known", "VAR v = IF(TRUE(), 1, 0) RETURN v", type: "Double"),
            New("Hidden", "VAR v = IF(TRUE(), 1, 0) RETURN v", hidden: true),
            New("OutsideScope", "IF(TRUE(), 1, 0)")
        ], 0, 1)], []);
        var report = new MeasureInspectionEngine().Inspect(snapshot);
        var s = report.Summary;
        Assert.Equal(7, s.Visible);
        Assert.Equal(6, s.UnknownTypes);
        Assert.Equal(5, s.VarReturnAnalyzed);
        Assert.Equal(2, s.VarReturnLikelyNumeric);
        Assert.Equal(1, s.VarReturnLikelyNonNumeric);
        Assert.Equal(2, s.VarReturnUnresolved);
        Assert.Equal(2, s.VarReturnLikelyNumericMissingFormat);
        Assert.Equal(2, s.VarReturnUnresolvedReasons.Sum(x => x.Count));
        Assert.Equal(2, s.VarReturnBlockedShapes.Sum(x => x.Count));
        Assert.Equal(StaticDaxTypeEvidenceKind.NotAssessed,
            Assert.Single(report.Measures, x => x.MeasureName == "Known").VarReturnEvidence);
        Assert.Equal(StaticDaxTypeEvidenceKind.NotAssessed,
            Assert.Single(report.Measures, x => x.MeasureName == "OutsideScope").VarReturnEvidence);
        Assert.Single(new UnformattedNumericMeasureRule().Evaluate(snapshot));
        var audit = new ModelQualityAuditEngine(ModelQualityRulePack.CreateDefault()).Run(snapshot);
        var coverage = ModelAuditCoverageAnalyzer.Analyze(snapshot, audit);
        Assert.Equal(1, coverage.MeasureFormatting!.CheckedNumeric);
        Assert.Equal(6, coverage.MeasureFormatting.UnknownType);
        Assert.Equal(1, coverage.Sm002Assessment!.ConfirmedWarnings);

        using var writer = new StringWriter();
        new MeasureInspectionRenderer(writer).Render("C:/private/Example.pbip", report);
        var output = writer.ToString();
        Assert.Contains("VAR / RETURN evidence", output);
        Assert.Contains("likely numeric 2", output);
        Assert.Contains("likely nonnumeric 1", output);
        Assert.DoesNotContain("SecretTable", output);
        Assert.DoesNotContain("Sensitive", output);
        Assert.DoesNotContain("SecretModel", output);
        Assert.DoesNotContain("NumericSwitch", output);
        Assert.DoesNotContain("COALESCE(", output);
        Assert.DoesNotContain('\u001b', output);
    }

    private static TomMeasureSnapshot New(string name, string expression,
        string? format = null, bool hidden = false, string type = "Unknown") =>
        new(name, expression, format, hidden, type);
}

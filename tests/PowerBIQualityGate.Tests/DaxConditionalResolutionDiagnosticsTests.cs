using PowerBIQualityGate.Cli.Rendering;
using PowerBIQualityGate.Core.Inspection;
using PowerBIQualityGate.Core.Quality;
using PowerBIQualityGate.Core.Quality.Rules;

namespace PowerBIQualityGate.Tests;

public sealed class DaxConditionalResolutionDiagnosticsTests
{
    [Theory]
    [InlineData("VAR r = IF(TRUE(), 1, [Sensitive]) RETURN r", DaxRootShapeKind.ReferenceOrOperator)]
    [InlineData("VAR r = SWITCH(TRUE(), TRUE(), [Sensitive], 0) RETURN r", DaxRootShapeKind.ReferenceOrOperator)]
    [InlineData("VAR r = COALESCE([Sensitive], 0) RETURN r", DaxRootShapeKind.ReferenceOrOperator)]
    [InlineData("VAR r = IF(TRUE(), 0, DIVIDE(1, 2)) RETURN r", DaxRootShapeKind.Divide)]
    [InlineData("VAR r = IF(TRUE(), CALCULATE([Sensitive]), 0) RETURN r", DaxRootShapeKind.Calculate)]
    [InlineData("VAR r = IF(TRUE(), FOO(1), 0) RETURN r", DaxRootShapeKind.OtherFunction)]
    [InlineData("VAR r = IF(TRUE(), unknown, 0) RETURN r", DaxRootShapeKind.ReferenceOrOperator)]
    [InlineData("VAR r = IF(TRUE(), CALCULATETABLE(Data), 0) RETURN r", DaxRootShapeKind.Calculate)]
    [InlineData("VAR r = IF(TRUE(), BLANK(), [Sensitive]) RETURN r", DaxRootShapeKind.ReferenceOrOperator)]
    [InlineData("VAR r = COALESCE([Sensitive], BLANK()) RETURN r", DaxRootShapeKind.ReferenceOrOperator)]
    [InlineData("VAR r = IF(TRUE(), SUM(Data[Value]), [Sensitive]) RETURN r", DaxRootShapeKind.ReferenceOrOperator)]
    [InlineData("VAR x = 3 RETURN IF(TRUE(), x, [Sensitive])", DaxRootShapeKind.ReferenceOrOperator)]
    public void UnknownResultBranchIsReportedWithoutExposingExpression(
        string dax, DaxRootShapeKind blockedBranch)
    {
        var actual = DaxVarReturnEvidence.AnalyzeDetailed(dax);
        Assert.Equal(StaticDaxTypeEvidenceKind.Unresolved, actual.Evidence);
        Assert.Equal(DaxConditionalResolutionKind.UnresolvedResultBranch, actual.ConditionalReason);
        Assert.Equal(blockedBranch, actual.FirstUnresolvedConditionalBranchShape);
        Assert.Equal(DaxRootShapeKind.Conditional, actual.BlockedRootShape);
        Assert.Equal(DaxVarReturnEvidence.Analyze(dax), actual.Evidence);
    }

    [Theory]
    [InlineData("VAR r = IF(TRUE(), 1, \"text\") RETURN r")]
    [InlineData("VAR r = SWITCH(TRUE(), TRUE(), 1, FALSE(), \"text\") RETURN r")]
    [InlineData("VAR r = COALESCE(1, \"text\") RETURN r")]
    [InlineData("VAR r = IF(TRUE(), TRUE(), 1) RETURN r")]
    [InlineData("VAR r = IF(TRUE(), SUM(Data[Value]), \"text\") RETURN r")]
    public void MixedKnownTypesHaveDistinctReason(string dax)
    {
        var actual = DaxVarReturnEvidence.AnalyzeDetailed(dax);
        Assert.Equal(StaticDaxTypeEvidenceKind.Unresolved, actual.Evidence);
        Assert.Equal(DaxConditionalResolutionKind.MixedResultTypes, actual.ConditionalReason);
        Assert.Equal(DaxRootShapeKind.NotApplicable, actual.FirstUnresolvedConditionalBranchShape);
        Assert.Equal(DaxRootShapeKind.Conditional, actual.BlockedRootShape);
    }

    [Theory]
    [InlineData("VAR r = IF(TRUE(), BLANK(), BLANK()) RETURN r")]
    [InlineData("VAR r = IF(TRUE(), BLANK()) RETURN r")]
    [InlineData("VAR r = SWITCH(TRUE(), TRUE(), BLANK(), BLANK()) RETURN r")]
    [InlineData("VAR r = COALESCE(BLANK(), BLANK()) RETURN r")]
    public void OnlyBlankResultsHaveDistinctReason(string dax)
    {
        var actual = DaxVarReturnEvidence.AnalyzeDetailed(dax);
        Assert.Equal(StaticDaxTypeEvidenceKind.Unresolved, actual.Evidence);
        Assert.Equal(DaxConditionalResolutionKind.OnlyBlankResults, actual.ConditionalReason);
        Assert.Equal(DaxRootShapeKind.NotApplicable, actual.FirstUnresolvedConditionalBranchShape);
        Assert.Equal(DaxRootShapeKind.Conditional, actual.BlockedRootShape);
    }

    [Theory]
    [InlineData("VAR r = IF(TRUE(), 1,) RETURN r")]
    [InlineData("VAR r = IFERROR(1, 0) RETURN r")]
    [InlineData("VAR r = IF(TRUE(), 1, 0) + 1 RETURN r")]
    [InlineData("VAR r = SWITCH(TRUE(), 1) RETURN r")]
    [InlineData("VAR r = COALESCE(1,) RETURN r")]
    public void UnsupportedCallFormsAreNotSyntaxErrorClaims(string dax)
    {
        var actual = DaxVarReturnEvidence.AnalyzeDetailed(dax);
        Assert.Equal(StaticDaxTypeEvidenceKind.Unresolved, actual.Evidence);
        Assert.Equal(DaxConditionalResolutionKind.UnsupportedCallShape, actual.ConditionalReason);
        Assert.Equal(DaxRootShapeKind.NotApplicable, actual.FirstUnresolvedConditionalBranchShape);
        Assert.Equal(DaxRootShapeKind.Conditional, actual.BlockedRootShape);
    }

    [Theory]
    [InlineData("VAR r = IF(TRUE(), 1, 0) RETURN r", StaticDaxTypeEvidenceKind.LikelyNumeric)]
    [InlineData("VAR r = IF(TRUE(), \"a\", \"b\") RETURN r", StaticDaxTypeEvidenceKind.LikelyNonNumeric)]
    [InlineData("VAR r = COALESCE(1, BLANK()) RETURN r", StaticDaxTypeEvidenceKind.LikelyNumeric)]
    [InlineData("VAR r = CALCULATE(SUM(Data[Value])) RETURN r", StaticDaxTypeEvidenceKind.LikelyNumeric)]
    [InlineData("VAR r = DIVIDE(1, 2) RETURN r", StaticDaxTypeEvidenceKind.Unresolved)]
    public void NonConditionalOrResolvedCasesHaveNoConditionalReason(string dax,
        StaticDaxTypeEvidenceKind expected)
    {
        var actual = DaxVarReturnEvidence.AnalyzeDetailed(dax);
        Assert.Equal(expected, actual.Evidence);
        Assert.Equal(DaxConditionalResolutionKind.NotApplicable, actual.ConditionalReason);
        Assert.Equal(DaxRootShapeKind.NotApplicable, actual.FirstUnresolvedConditionalBranchShape);
    }

    [Fact]
    public void ConditionalReasonsExactlyPartitionBlockedConditionalRootShapes()
    {
        var snapshot = Model(
            New("UnknownRef", "VAR r = IF(TRUE(), 1, [Secret]) RETURN r"),
            New("MixedKinds", "VAR r = SWITCH(TRUE(), TRUE(), 1, FALSE(), \"private\") RETURN r"),
            New("OnlyBlank", "VAR r = COALESCE(BLANK(), BLANK()) RETURN r"),
            New("UnsupportedCall", "VAR r = IF(TRUE(), 1,) RETURN r"),
            New("WrappedUnknown", "VAR r = IF(TRUE(), CALCULATE([Private]), 1) RETURN r", "0.00"),
            New("KnownVar", "VAR r = IF(TRUE(), 1, 0) RETURN r"),
            New("KnownTom", "VAR r = IF(TRUE(), 1, [Secret]) RETURN r", type: "Double"),
            New("OutsideScope", "IF(TRUE(), 1, [Secret])"),
            New("Hidden", "VAR r = IF(TRUE(), 1, [Secret]) RETURN r", hidden: true));

        var report = new MeasureInspectionEngine().Inspect(snapshot);
        var s = report.Summary;
        Assert.Equal(8, s.Visible);
        Assert.Equal(6, s.VarReturnAnalyzed);
        Assert.Equal(1, s.VarReturnLikelyNumeric);
        Assert.Equal(5, s.VarReturnUnresolved);
        Assert.Equal(5, s.VarReturnConditionalReasons.Sum(x => x.Count));
        Assert.Equal(4, s.VarReturnConditionalReasons.Sum(x => x.MissingFormat));
        Assert.Equal(5, Assert.Single(s.VarReturnBlockedShapes,
            x => x.Shape == DaxRootShapeKind.Conditional).Count);
        Assert.Contains(s.VarReturnConditionalReasons,
            x => x.Reason == DaxConditionalResolutionKind.UnresolvedResultBranch &&
                x.Count == 2 && x.MissingFormat == 1);
        Assert.Contains(s.VarReturnConditionalReasons,
            x => x.Reason == DaxConditionalResolutionKind.MixedResultTypes && x.Count == 1);
        Assert.Contains(s.VarReturnConditionalReasons,
            x => x.Reason == DaxConditionalResolutionKind.OnlyBlankResults && x.Count == 1);
        Assert.Contains(s.VarReturnConditionalReasons,
            x => x.Reason == DaxConditionalResolutionKind.UnsupportedCallShape && x.Count == 1);
        Assert.Equal(2, s.VarReturnConditionalUnknownBranchShapes.Sum(x => x.Count));
        Assert.Equal(1, s.VarReturnConditionalUnknownBranchShapes.Sum(x => x.MissingFormat));
        Assert.Contains(s.VarReturnConditionalUnknownBranchShapes, x =>
            x.Shape == DaxRootShapeKind.ReferenceOrOperator && x.Count == 1 && x.MissingFormat == 1);
        Assert.Contains(s.VarReturnConditionalUnknownBranchShapes, x =>
            x.Shape == DaxRootShapeKind.Calculate && x.Count == 1 && x.MissingFormat == 0);
        Assert.Equal(DaxConditionalResolutionKind.NotApplicable,
            Assert.Single(report.Measures, x => x.MeasureName == "KnownTom").VarReturnConditionalReason);
        Assert.Equal(DaxConditionalResolutionKind.NotApplicable,
            Assert.Single(report.Measures, x => x.MeasureName == "Hidden").VarReturnConditionalReason);
        Assert.Equal(DaxConditionalResolutionKind.NotApplicable,
            Assert.Single(report.Measures, x => x.MeasureName == "OutsideScope").VarReturnConditionalReason);
        Assert.Equal(1, s.VarReturnLikelyNumericMissingFormat);
        Assert.Single(new UnformattedNumericMeasureRule().Evaluate(snapshot));
        var audit = new ModelQualityAuditEngine(ModelQualityRulePack.CreateDefault()).Run(snapshot);
        var coverage = ModelAuditCoverageAnalyzer.Analyze(snapshot, audit);
        Assert.Equal(1, coverage.MeasureFormatting!.CheckedNumeric);
        Assert.Equal(7, coverage.MeasureFormatting.UnknownType);
        Assert.Equal(1, coverage.Sm002Assessment!.ConfirmedWarnings);
    }

    [Fact]
    public void RendererUsesOnlyFixedAggregateLabelsAndCounts()
    {
        var snapshot = Model(
            New("SecretAmount", "VAR r = IF(TRUE(), 1, [PrivateRevenue]) RETURN r"),
            New("HighlySensitive", "VAR x = COALESCE(1, \"InternalLabel\") RETURN x"));
        using var writer = new StringWriter();
        new MeasureInspectionRenderer(writer).Render("C:/Private/Example.pbip",
            new MeasureInspectionEngine().Inspect(snapshot));
        var output = writer.ToString();
        Assert.Contains("VAR / RETURN conditional unresolved reasons", output);
        Assert.Contains("Unresolved result branch: 1 / 1", output);
        Assert.Contains("Mixed numeric / nonnumeric result kinds: 1 / 1", output);
        Assert.Contains("VAR / RETURN first unresolved conditional branch shapes", output);
        Assert.Contains("Reference / operator / literal: 1 / 1", output);
        Assert.DoesNotContain("SecretAmount", output);
        Assert.DoesNotContain("HighlySensitive", output);
        Assert.DoesNotContain("PrivateRevenue", output);
        Assert.DoesNotContain("InternalLabel", output);
        Assert.DoesNotContain("COALESCE(", output);
        Assert.DoesNotContain('\u001b', output);
    }

    private static TomModelSnapshot Model(params TomMeasureSnapshot[] measures) => new(
        "Synthetic", 1600,
        [new TomTableSnapshot("PrivateTable", false, [], measures, 0, 1)], []);

    private static TomMeasureSnapshot New(string name, string expression,
        string? format = null, bool hidden = false, string type = "Unknown") =>
        new(name, expression, format, hidden, type);
}

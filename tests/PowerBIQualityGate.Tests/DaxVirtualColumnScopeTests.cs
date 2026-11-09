using PowerBIQualityGate.Core.Inspection;

namespace PowerBIQualityGate.Tests;

public sealed class DaxVirtualColumnScopeTests
{
    [Theory]
    [InlineData("SUMX(ADDCOLUMNS('Sales', \"Virtual\", 'Sales'[Amount]), [Virtual])")]
    [InlineData("MAXX(FILTER(ADDCOLUMNS('Sales', \"Virtual\", 1), [Virtual] > 0), [Virtual])")]
    [InlineData("MAXX(TOPN(1, ADDCOLUMNS('Sales', \"Virtual\", 1), [Virtual], DESC), [Virtual])")]
    [InlineData("SUMX(SELECTCOLUMNS('Sales', \"Virtual\", 'Sales'[Amount]), [Virtual])")]
    [InlineData("AVERAGEX(ADDCOLUMNS('Sales', \"Virtual\", 1), [Virtual])")]
    public void InlineVirtualColumnsAreRecognizedOnlyInConsumerRowScope(string dax)
    {
        var result = Check(dax);
        Assert.Empty(result.Candidates);
        Assert.Equal(0, result.SkippedExpressions);
    }

    [Fact]
    public void VariableTableLineageThroughFilterAndTopnIsRecognized()
    {
        var dax = """
            VAR T0 = ADDCOLUMNS('Sales', "Virtual", 'Sales'[Amount], "Another", 1)
            VAR T = TOPN(1, FILTER(T0, NOT ISBLANK([Virtual])), [Another], DESC)
            RETURN MAXX(T, [Virtual] + [Another])
            """;
        Assert.Empty(Check(dax).Candidates);
    }

    [Fact]
    public void CaseInsensitiveAliasAndVarNamesAreRecognized()
    {
        Assert.Empty(Check("VAR temp = ADDCOLUMNS('Sales', \"Virtual\", 1) RETURN MAXX(TEMP, [vIrTuAl])").Candidates);
    }

    [Fact]
    public void EscapedDoubleQuotesInVirtualAliasAreSupported()
    {
        Assert.Empty(Check("SUMX(ADDCOLUMNS('Sales', \"Alias\"\"One\", 1), [Alias\"One])").Candidates);
    }

    [Fact]
    public void AliasOutsideIteratingRowScopeIsStillFlagged()
    {
        var report = Check("SUMX(ADDCOLUMNS('Sales', \"Virtual\", 1), [Virtual]) + [Virtual]");
        Assert.Equal("Virtual", Assert.Single(report.Candidates).ReferenceName);
    }

    [Fact]
    public void AliasFromDifferentTableVariableIsStillFlagged()
    {
        var report = Check("VAR A = ADDCOLUMNS('Sales', \"Virtual\", 1) " +
            "VAR B = 'Sales' RETURN MAXX(B, [Virtual])");
        Assert.Equal("Virtual", Assert.Single(report.Candidates).ReferenceName);
    }

    [Fact]
    public void AliasInitializerDoesNotInheritNewlyDefinedAlias()
    {
        var report = Check("SUMX(ADDCOLUMNS('Sales', \"Virtual\", [Virtual] + 1), [Virtual])");
        Assert.Equal("Virtual", Assert.Single(report.Candidates).ReferenceName);
    }

    [Fact]
    public void UnknownReferenceAlongsideVirtualColumnStillAppears()
    {
        var report = Check("MAXX(ADDCOLUMNS('Sales', \"Virtual\", 1), [Virtual] + [ReallyMissing])");
        Assert.Equal("ReallyMissing", Assert.Single(report.Candidates).ReferenceName);
    }

    [Fact]
    public void QualifiedMissingColumnCannotBeSuppressedByVirtualAlias()
    {
        var report = Check("MAXX(ADDCOLUMNS('Sales', \"Virtual\", 1), 'Sales'[Virtual])");
        var issue = Assert.Single(report.Candidates);
        Assert.Equal(DaxDependencyCandidateKind.MissingQualifiedMember, issue.Kind);
    }

    [Fact]
    public void AliasNeverLeaksBetweenDifferentMeasureDefinitions()
    {
        var report = CheckMultiple(
            Measure("WithAlias", "SUMX(ADDCOLUMNS('Sales', \"Virtual\", 1), [Virtual])"),
            Measure("Other", "[Virtual]"));
        Assert.Equal("Other", Assert.Single(report.Candidates).MeasureName);
    }

    [Fact]
    public void UnknownAliasInOneBranchDoesNotInheritFromOtherBranch()
    {
        var report = Check("IF(TRUE(), SUMX(ADDCOLUMNS('Sales', \"Virtual\", 1), [Virtual]), [Virtual])");
        Assert.Equal("Virtual", Assert.Single(report.Candidates).ReferenceName);
    }

    [Fact]
    public void NestedTableVariablesAndAliasesRemainLocalToBlock()
    {
        var report = Check("VAR T = ADDCOLUMNS('Sales', \"Virtual\", 1) RETURN MAXX(T, [Virtual]) + [Virtual]");
        Assert.Equal("Virtual", Assert.Single(report.Candidates).ReferenceName);
    }

    [Fact]
    public void SecondAliasDoesNotResolveInsideFirstExpression()
    {
        var report = Check("MAXX(ADDCOLUMNS('Sales', \"One\", [Two], \"Two\", 7), [Two])");
        Assert.Equal("Two", Assert.Single(report.Candidates).ReferenceName);
    }

    [Fact]
    public void CommentsAndStringsCannotDefineVirtualColumns()
    {
        var report = Check("SUMX('Sales', [Virtual]) /* ADDCOLUMNS('Sales', \"Virtual\", 1) */ " +
            "+ LEN(\"ADDCOLUMNS('Sales', 1)\")");
        Assert.Equal("Virtual", Assert.Single(report.Candidates).ReferenceName);
    }

    [Fact]
    public void MalformedVirtualAliasIsNotTreatedAsProvenBinding()
    {
        // An unsupported non-literal alias name must not suppress a finding.
        var report = Check("MAXX(ADDCOLUMNS('Sales', [Virtual], 1), [Virtual])");
        Assert.Equal("Virtual", Assert.Single(report.Candidates).ReferenceName);
    }

    [Fact]
    public void ValidExistingPhysicalReferencesRemainUnchanged()
    {
        var report = Check("SUMX(ADDCOLUMNS('Sales', \"Virtual\", [Amount]), [Amount] + [Virtual])");
        Assert.Empty(report.Candidates);
    }

    private static DaxDependencyReport Check(string dax) => CheckMultiple(Measure("Target", dax));

    private static DaxDependencyReport CheckMultiple(params TomMeasureSnapshot[] measures) =>
        DaxDependencyInspector.Inspect(new TomModelSnapshot("Synthetic", 1600,
            [new TomTableSnapshot("Sales", false,
                [new TomColumnSnapshot("Amount", "Double", "Data", false, null)],
                measures, 0, 1)], []));

    private static TomMeasureSnapshot Measure(string name, string dax) =>
        new(name, dax, null, false);
}

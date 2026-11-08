using PowerBIQualityGate.Cli;
using PowerBIQualityGate.Cli.Rendering;
using PowerBIQualityGate.Core.Inspection;

namespace PowerBIQualityGate.Tests;

public sealed class DaxDependencyIntegrityTests
{
    [Theory]
    [InlineData("VAR n = 1 RETURN [Existing]")]
    [InlineData("VAR n = 1 RETURN\n[Existing]")]
    [InlineData("VAR n = 1 ReTuRn [Existing]")]
    public void ReturnKeywordIsNotMistakenForATableName(string expression)
    {
        var report = Check(Table("T", [], Measure("Existing", "1"),
            Measure("Uses", expression)));
        Assert.Empty(report.Candidates);
        Assert.Equal(0, report.SkippedExpressions);
    }

    [Fact]
    public void UnknownBracketNameAfterReturnIsUnqualifiedNotAnUnknownTable()
    {
        var report = Check(Table("T", [],
            Measure("Uses", "VAR n = 1 RETURN [Missing]")));
        var candidate = Assert.Single(report.Candidates);
        Assert.Equal(DaxDependencyCandidateKind.UnresolvedUnqualifiedReference,
            candidate.Kind);
        Assert.Null(candidate.ReferenceTable);
        Assert.Equal(0, report.Count(DaxDependencyCandidateKind.MissingTable));
    }

    [Fact]
    public void QuotedTableLiterallyNamedReturnCanStillBeResolved()
    {
        var report = Check(Table("RETURN", ["Physical"]),
            Table("T", [], Measure("Uses", "'RETURN'[Physical]")));
        Assert.Empty(report.Candidates);
    }

    [Fact]
    public void DetectsUnknownUnqualifiedAndQualifiedReferencesButDoesNotClaimInvalidDax()
    {
        var report = Check(
            Table("Sales", ["Amount"],
                Measure("Broken", "VAR q = [MissingMeasure] RETURN q + 'Sales'[MissingColumn] + 'Gone'[Value]"),
                Measure("Working", "SUM('Sales'[Amount]) + [Total]"),
                Measure("Total", "SUM('Sales'[Amount])")));
        Assert.Equal(3, report.InspectedMeasures);
        Assert.Equal(0, report.SkippedExpressions);
        Assert.Equal(3, report.Candidates.Count);
        Assert.Equal(1, report.AffectedMeasures);
        Assert.Equal(1, report.Count(DaxDependencyCandidateKind.MissingTable));
        Assert.Equal(1, report.Count(DaxDependencyCandidateKind.MissingQualifiedMember));
        Assert.Equal(1, report.Count(DaxDependencyCandidateKind.UnresolvedUnqualifiedReference));
        Assert.All(report.Candidates, item => Assert.Equal("Broken", item.MeasureName));
    }

    [Fact]
    public void UnqualifiedColumnMatchingAnywhereIsNotAssumedMissing()
    {
        var report = Check(
            Table("Sales", ["Price"], Measure("Valid", "[Price] + [Total] + Sales[Price]")),
            Table("Totals", [], Measure("Total", "42")));
        Assert.Empty(report.Candidates);
    }

    [Fact]
    public void QualifiedReferenceMustBelongToSpecifiedTable()
    {
        var report = Check(
            Table("A", ["Known"], Measure("WrongOwner", "'A'[FromB] + 'B'[FromB]")),
            Table("B", ["FromB"]));
        var finding = Assert.Single(report.Candidates);
        Assert.Equal(DaxDependencyCandidateKind.MissingQualifiedMember, finding.Kind);
        Assert.Equal("A", finding.ReferenceTable);
    }

    [Fact]
    public void EscapedTableNamesAndCaseInsensitiveLookupsAreRecognized()
    {
        var report = Check(
            Table("O'Brien", ["Revenue"], Measure("Quoted", "SUM('O''Brien'[revenue])")),
            Table("Sales", ["Net]Amount"], Measure("Escaped", "SUM('Sales'[Net]]Amount])")));
        Assert.Empty(report.Candidates);
        Assert.Equal(0, report.SkippedExpressions);
    }

    [Fact]
    public void TextLiteralsAndCommentsAreNeverTreatedAsReferences()
    {
        var report = Check(Table("T", ["Known"],
            Measure("Quoted", "\"NotReal[Field]\" & \"escaped \"\"[Name]\"\"\""),
            Measure("Comments", "SUM(T[Known]) // 'Ghost'[Missing]\n + 1 /* [Hidden] 'Other'[Missing] */"),
            Measure("Line", "1 -- [Skipped]\n + T[Known]")));
        Assert.Empty(report.Candidates);
        Assert.Equal(0, report.SkippedExpressions);
    }

    [Fact]
    public void RepeatedReferencesProduceOnlyOneFindingPerMeasure()
    {
        var report = Check(Table("T", [], Measure("Repeated", "[NotFound] + [NotFound] + [notfound]")));
        Assert.Single(report.Candidates);
    }

    [Theory]
    [InlineData("SUM('Unclosed[Value])")]
    [InlineData("SUM('T'[Incomplete)")]
    [InlineData("SUM(T[Value]) /* not closed")]
    [InlineData("SUM([Missing]")]
    [InlineData("([Missing])}")]
    [InlineData("\"not closed [Field]")]
    public void MalformedOrTruncatedLexicalShapesAreSkippedCompletely(string dax)
    {
        var report = Check(Table("T", [], Measure("Broken", dax)));
        Assert.Equal(1, report.SkippedExpressions);
        Assert.Empty(report.Candidates);
    }

    [Fact]
    public void ExpressionsAboveSafeLengthAreSkipped()
    {
        var report = Check(Table("T", [], Measure("Huge", new string('a', 66000) + "[Missing]")));
        Assert.Equal(1, report.SkippedExpressions);
        Assert.Empty(report.Candidates);
    }

    [Fact]
    public void ReportIncludesHiddenMeasuresButDefaultRendererPrintsOnlySafeCounts()
    {
        var report = Check(Table("PrivateTable", [],
            Measure("ConfidentialHiddenMeasure", "[HighlySecretValue]", hidden: true)));
        Assert.Single(report.Candidates);
        using var writer = new StringWriter();
        new DaxDependencySummaryRenderer(writer).Render(report);
        var output = writer.ToString();
        Assert.Contains("Candidate references: 1", output);
        Assert.Contains("Unresolved unqualified [Name]: 1", output);
        Assert.DoesNotContain("HighlySecretValue", output);
        Assert.DoesNotContain("PrivateTable", output);
        Assert.DoesNotContain("ConfidentialHiddenMeasure", output);
        Assert.DoesNotContain('\u001b', output);
    }

    [Fact]
    public void PrivateReviewHasIdentifiersButNoFormulasAndNeverOverwrites()
    {
        var report = Check(Table("T", [], Measure("=RiskyName", "[MissingPrivateReference]")));
        WithTempDirectory(dir =>
        {
            var path = Path.Combine(dir, "review.csv");
            Assert.Equal(1, DaxDependencyReviewWriter.Write(path, "P01", report));
            var content = File.ReadAllText(path);
            Assert.Contains("\"'=RiskyName\"", content);
            Assert.Contains("MissingPrivateReference", content);
            Assert.Contains("unresolved_unqualified", content);
            Assert.DoesNotContain("[MissingPrivateReference]", content);
            Assert.Throws<IOException>(() => DaxDependencyReviewWriter.Write(path, "P01", report));
        });
    }

    [Fact]
    public void PrivateReviewRejectsGitAndUnapprovedCaseIds()
    {
        var report = Check(Table("T", [], Measure("A", "[Missing]")));
        WithTempDirectory(dir =>
        {
            var path = Path.Combine(dir, "review.csv");
            Assert.Throws<ArgumentException>(() =>
                DaxDependencyReviewWriter.Write(path, "MyActualCompany", report));
            Directory.CreateDirectory(Path.Combine(dir, ".git"));
            Assert.Throws<ArgumentException>(() => DaxDependencyReviewWriter.Write(path, "P01", report));
            Assert.False(File.Exists(path));
        });
    }

    [Fact]
    public void CliRequiresExplicitOptInAndReturnsAdvisorySuccess()
    {
        var root = CliApplication.CreateRootCommand();
        var project = ExampleProject();
        var missingMeasures = root.Parse(["inspect", project, "--dependencies"]);
        Assert.Empty(missingMeasures.Errors);
        Assert.Equal(2, missingMeasures.Invoke());

        var standard = root.Parse(["inspect", project, "--measures"]);
        Assert.Empty(standard.Errors);
        Assert.Equal(0, standard.Invoke());

        var optIn = root.Parse(["inspect", project, "--measures", "--dependencies"]);
        Assert.Empty(optIn.Errors);
        Assert.Equal(0, optIn.Invoke());
    }

    [Fact]
    public void CliCreatesPrivateReviewOnlyWhenExplicitlyRequested()
    {
        WithTempDirectory(dir =>
        {
            var path = Path.Combine(dir, "private.csv");
            var root = CliApplication.CreateRootCommand();
            var reject = root.Parse(["inspect", ExampleProject(), "--measures",
                "--dependency-review", path, "--case-id", "P01"]);
            Assert.Empty(reject.Errors);
            Assert.Equal(2, reject.Invoke());
            Assert.False(File.Exists(path));

            var accept = root.Parse(["inspect", ExampleProject(), "--measures",
                "--dependencies", "--dependency-review", path, "--case-id", "P01"]);
            Assert.Empty(accept.Errors);
            Assert.Equal(0, accept.Invoke());
            Assert.True(File.Exists(path));
            Assert.StartsWith("case_id,measure_table,measure_name,", File.ReadAllText(path));
        });
    }

    private static DaxDependencyReport Check(params TomTableSnapshot[] tables) =>
        DaxDependencyInspector.Inspect(new TomModelSnapshot("PrivateModel", 1600, tables, []));

    private static TomTableSnapshot Table(string name, string[] columns,
        params TomMeasureSnapshot[] measures) =>
        new(name, false,
            columns.Select(column => new TomColumnSnapshot(column, "Double", "Data", false, null)).ToArray(),
            measures, 0, 1);

    private static TomMeasureSnapshot Measure(string name, string dax, bool hidden = false) =>
        new(name, dax, null, hidden);

    private static void WithTempDirectory(Action<string> action)
    {
        var dir = Path.Combine(Path.GetTempPath(), "pbiq-dependencies-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try { action(dir); }
        finally { Directory.Delete(dir, recursive: true); }
    }

    private static string ExampleProject()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PowerBIQualityGate.slnx")))
                return Path.Combine(directory.FullName, "examples", "valid-pbir-tmdl");
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Solution root not found.");
    }
}

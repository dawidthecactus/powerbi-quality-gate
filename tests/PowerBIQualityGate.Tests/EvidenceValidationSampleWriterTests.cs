using PowerBIQualityGate.Cli;
using PowerBIQualityGate.Cli.Rendering;
using PowerBIQualityGate.Core.Inspection;

namespace PowerBIQualityGate.Tests;

public sealed class EvidenceValidationSampleWriterTests
{
    [Fact]
    public void ExportsPrivateNamesAndPredictionsButNeverExpressions()
    {
        var report = Report(
            Measure("Amount", "SUM('T'[SuperSecretColumn])"),
            Measure("VarLabel", "VAR value = \"HiddenPrivateValue\" RETURN value"),
            Measure("Unresolved", "[UnknownPrivateMeasure]"),
            Measure("TOMKnown", "SUM('T'[SuperSecretColumn])", type: "Int64"),
            Measure("Hidden", "SUM('T'[SuperSecretColumn])", hidden: true));
        WithTempDir(folder =>
        {
            var path = Path.Combine(folder, "private-evidence.csv");
            Assert.Equal(2, EvidenceValidationSampleWriter.Write(path, "P01", report));
            var csv = File.ReadAllText(path);
            Assert.Contains("Amount", csv);
            Assert.Contains("VarLabel", csv);
            Assert.Contains("\"static\",\"numeric\"", csv);
            Assert.Contains("\"var\",\"nonnumeric\"", csv);
            Assert.DoesNotContain("SuperSecretColumn", csv);
            Assert.DoesNotContain("HiddenPrivateValue", csv);
            Assert.DoesNotContain("UnknownPrivateMeasure", csv);
            Assert.DoesNotContain("TOMKnown", csv);
            Assert.DoesNotContain("\"Hidden\"", csv);
            Assert.DoesNotContain("SUM(", csv);
            Assert.DoesNotContain("RETURN", csv);
        });
    }

    [Fact]
    public void SelectionIsDeterministicAndBoundedPerSourceAndPredictedKind()
    {
        var measures = Enumerable.Range(0, 30)
            .Select(i => Measure($"Numeric{i}", $"{i}"))
            .Concat(Enumerable.Range(0, 30).Select(i =>
                Measure($"Text{i}", $"\"literal{i}\"")))
            .ToArray();
        var report = Report(measures);
        WithTempDir(folder =>
        {
            var first = Path.Combine(folder, "first.csv");
            var second = Path.Combine(folder, "second.csv");
            Assert.Equal(8, EvidenceValidationSampleWriter.Write(first, "P01", report, 4));
            Assert.Equal(8, EvidenceValidationSampleWriter.Write(second, "P01", report, 4));
            Assert.Equal(File.ReadAllText(first), File.ReadAllText(second));
            Assert.Equal(9, File.ReadAllLines(first).Length); // header + eight rows
        });
    }

    [Fact]
    public void SampleOutputDoesNotOverwriteAnExistingReview()
    {
        var report = Report(Measure("Value", "42"));
        WithTempDir(folder =>
        {
            var path = Path.Combine(folder, "sample.csv");
            File.WriteAllText(path, "Never overwrite private review");
            Assert.Throws<IOException>(() =>
                EvidenceValidationSampleWriter.Write(path, "P01", report));
            Assert.Equal("Never overwrite private review", File.ReadAllText(path));
        });
    }

    [Fact]
    public void RejectsPrivateReviewDestinationInsideGitIncludingWorktreeMarker()
    {
        var report = Report(Measure("Value", "42"));
        WithTempDir(folder =>
        {
            Directory.CreateDirectory(Path.Combine(folder, ".git"));
            var output = Path.Combine(folder, "sample.csv");
            Assert.Throws<ArgumentException>(() =>
                EvidenceValidationSampleWriter.Write(output, "P01", report));
            Directory.Delete(Path.Combine(folder, ".git"));
            File.WriteAllText(Path.Combine(folder, ".git"), "gitdir: somewhere");
            Assert.Throws<ArgumentException>(() =>
                EvidenceValidationSampleWriter.Write(output, "P01", report));
        });
    }

    [Fact]
    public void InvalidCaseIdAndExtensionsAreRejectedBeforeFileCreation()
    {
        var report = Report(Measure("Value", "42"));
        WithTempDir(folder =>
        {
            var path = Path.Combine(folder, "review.csv");
            Assert.Throws<ArgumentException>(() =>
                EvidenceValidationSampleWriter.Write(path, "PrivateModelName", report));
            Assert.Throws<ArgumentException>(() =>
                EvidenceValidationSampleWriter.Write(path, "P01", report, perStratum: 0));
            Assert.Throws<ArgumentException>(() =>
                EvidenceValidationSampleWriter.Write(Path.Combine(folder, "sample.txt"), "P01", report));
            Assert.False(File.Exists(path));
        });
    }

    [Fact]
    public void SpreadsheetFormulaLikeMeasureNamesAreEscaped()
    {
        var report = Report(Measure("=CMD(1)", "1"));
        WithTempDir(folder =>
        {
            var path = Path.Combine(folder, "sample.csv");
            EvidenceValidationSampleWriter.Write(path, "P01", report);
            Assert.Contains("\"'=CMD(1)\"", File.ReadAllText(path));
        });
    }

    [Fact]
    public void CliRequiresBothOptInSamplePathAndAnonymousCaseId()
    {
        var root = CliApplication.CreateRootCommand();
        var project = ExampleProject();
        WithTempDir(folder =>
        {
            var path = Path.Combine(folder, "sample.csv");
            var missingMeasures = root.Parse([
                "inspect", project, "--evidence-sample", path, "--case-id", "P01"]);
            Assert.Empty(missingMeasures.Errors);
            Assert.Equal(2, missingMeasures.Invoke());
            Assert.False(File.Exists(path));

            var missingCase = root.Parse([
                "inspect", project, "--measures", "--evidence-sample", path]);
            Assert.Empty(missingCase.Errors);
            Assert.Equal(2, missingCase.Invoke());
            Assert.False(File.Exists(path));
        });
    }

    [Fact]
    public void OptInCliSampleCreatesNewPrivateCsvAndKeepsDefaultInspectSuccessful()
    {
        WithTempDir(folder =>
        {
            var path = Path.Combine(folder, "sample.csv");
            var result = CliApplication.CreateRootCommand().Parse([
                "inspect", ExampleProject(), "--measures", "--evidence-sample", path,
                "--case-id", "P01", "--no-color"]);
            Assert.Empty(result.Errors);
            Assert.Equal(0, result.Invoke());
            Assert.True(File.Exists(path));
            Assert.StartsWith("case_id,sample_id,table_name,measure_name,", File.ReadAllText(path));
            var standard = CliApplication.CreateRootCommand().Parse([
                "inspect", ExampleProject(), "--measures", "--no-color"]);
            Assert.Empty(standard.Errors);
            Assert.Equal(0, standard.Invoke());
        });
    }

    private static MeasureInspectionReport Report(params TomMeasureSnapshot[] measures) =>
        new MeasureInspectionEngine().Inspect(new TomModelSnapshot("Private", 1600,
            [new TomTableSnapshot("T", false, [], measures, 0, 1)], []));

    private static TomMeasureSnapshot Measure(string name, string dax,
        string type = "Unknown", bool hidden = false) =>
        new(name, dax, null, hidden, type);

    private static void WithTempDir(Action<string> run)
    {
        var folder = Path.Combine(Path.GetTempPath(), "pbiq-evidence-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try { run(folder); }
        finally { Directory.Delete(folder, recursive: true); }
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

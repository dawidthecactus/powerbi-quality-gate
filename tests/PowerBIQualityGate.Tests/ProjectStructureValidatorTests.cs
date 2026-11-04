using PowerBIQualityGate.Core.Validation;

using Xunit;

namespace PowerBIQualityGate.Tests;

public class ProjectStructureValidatorTests
{
    private readonly ProjectStructureValidator _validator = new();

    [Fact]
    public void ValidTmdlProject_WithDifferentReportFolderName_Passes()
    {
        using var fixture = new ProjectFixture();

        var result = _validator.Validate(fixture.PbipPath);

        Assert.True(result.IsValid);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(fixture.ReportDirectory, result.ReportDirectory);
        Assert.Equal(ReportDefinitionFormat.Pbir, result.ReportFormat);
        Assert.Equal(SemanticModelDefinitionFormat.Tmdl, result.SemanticModelFormat);
    }

    [Fact]
    public void ThinReport_WithExternalModel_PassesWithoutLocalModel()
    {
        using var fixture = new ProjectFixture();
        fixture.Write(fixture.PbirManifest, """
            {"version":"4.0","datasetReference":{"byPath":null,"byConnection":{"connectionString":"semanticmodelid=example"}}}
            """);
        Directory.Delete(fixture.ModelDirectory, recursive: true);

        var result = _validator.Validate(fixture.PbipPath);

        Assert.True(result.IsValid);
        Assert.Equal(SemanticModelDefinitionFormat.External, result.SemanticModelFormat);
        Assert.Null(result.SemanticModelDirectory);
    }

    [Fact]
    public void LegacyReportAndTmslModel_Passes()
    {
        using var fixture = new ProjectFixture();
        Directory.Delete(Path.Combine(fixture.ReportDirectory, "definition"), recursive: true);
        fixture.Write(Path.Combine(fixture.ReportDirectory, "report.json"), "{}");
        fixture.Write(fixture.PbirManifest, """
            {"version":"1.0","datasetReference":{"byPath":{"path":"../Finance.SemanticModel"}}}
            """);
        Directory.Delete(Path.Combine(fixture.ModelDirectory, "definition"), recursive: true);
        fixture.Write(Path.Combine(fixture.ModelDirectory, "model.bim"), "{}");
        fixture.Write(fixture.PbismManifest, """{"version":"1.0"}""");

        var result = _validator.Validate(fixture.PbipPath);

        Assert.True(result.IsValid);
        Assert.Equal(ReportDefinitionFormat.PbirLegacy, result.ReportFormat);
        Assert.Equal(SemanticModelDefinitionFormat.Tmsl, result.SemanticModelFormat);
    }

    [Fact]
    public void MissingReportArtifact_IsReported()
    {
        using var fixture = new ProjectFixture();
        fixture.Write(fixture.PbipPath, """{"version":"1.0","artifacts":[]}""");

        AssertError(fixture, "PBI-003");
    }

    [Fact]
    public void MalformedPbip_IsReported()
    {
        using var fixture = new ProjectFixture();
        fixture.Write(fixture.PbipPath, "{not valid json");

        AssertError(fixture, "PBI-001");
    }

    [Fact]
    public void UnsafeReportPath_IsRejected()
    {
        using var fixture = new ProjectFixture();
        fixture.Write(fixture.PbipPath, """
            {"version":"1.0","artifacts":[{"report":{"path":"../Other.Report"}}]}
            """);

        AssertError(fixture, "PBI-004");
    }

    [Fact]
    public void MissingReportDirectory_IsReported()
    {
        using var fixture = new ProjectFixture();
        Directory.Delete(fixture.ReportDirectory, recursive: true);

        AssertError(fixture, "PBI-005");
    }

    [Fact]
    public void MissingPbirManifest_IsReported()
    {
        using var fixture = new ProjectFixture();
        File.Delete(fixture.PbirManifest);

        AssertError(fixture, "PBI-006");
    }

    [Fact]
    public void BothPbirAndLegacyReportFormats_AreRejected()
    {
        using var fixture = new ProjectFixture();
        fixture.Write(Path.Combine(fixture.ReportDirectory, "report.json"), "{}");

        AssertError(fixture, "PBI-008");
    }

    [Fact]
    public void MissingPbirReportJson_IsReported()
    {
        using var fixture = new ProjectFixture();
        File.Delete(Path.Combine(fixture.ReportDirectory, "definition", "report.json"));

        AssertError(fixture, "PBI-009");
    }

    [Fact]
    public void InvalidPageJson_IsReported()
    {
        using var fixture = new ProjectFixture();
        fixture.Write(Path.Combine(fixture.ReportDirectory, "definition", "pages", "Page1", "page.json"), "{");

        AssertError(fixture, "PBI-009");
    }

    [Fact]
    public void MissingVisualJson_IsReported()
    {
        using var fixture = new ProjectFixture();
        Directory.CreateDirectory(Path.Combine(
            fixture.ReportDirectory, "definition", "pages", "Page1", "visuals", "Chart1"));

        AssertError(fixture, "PBI-009");
    }

    [Fact]
    public void MissingLocalSemanticModel_IsReported()
    {
        using var fixture = new ProjectFixture();
        Directory.Delete(fixture.ModelDirectory, recursive: true);

        AssertError(fixture, "PBI-013");
    }

    [Fact]
    public void BothByPathAndByConnection_AreRejected()
    {
        using var fixture = new ProjectFixture();
        fixture.Write(fixture.PbirManifest, """
            {"version":"4.0","datasetReference":{"byPath":{"path":"../Finance.SemanticModel"},"byConnection":{"connectionString":"example"}}}
            """);

        AssertError(fixture, "PBI-011");
    }

    [Fact]
    public void AbsoluteModelReference_IsRejected()
    {
        using var fixture = new ProjectFixture();
        fixture.Write(fixture.PbirManifest, """
            {"version":"4.0","datasetReference":{"byPath":{"path":"/outside/model"}}}
            """);

        AssertError(fixture, "PBI-012");
    }

    [Fact]
    public void MissingPbism_IsReported()
    {
        using var fixture = new ProjectFixture();
        File.Delete(fixture.PbismManifest);

        AssertError(fixture, "PBI-014");
    }

    [Fact]
    public void TmdlWithLegacyModelVersion_IsRejected()
    {
        using var fixture = new ProjectFixture();
        fixture.Write(fixture.PbismManifest, """{"version":"1.0"}""");

        AssertError(fixture, "PBI-015");
    }

    [Fact]
    public void BothTmdlAndTmsl_AreRejected()
    {
        using var fixture = new ProjectFixture();
        fixture.Write(Path.Combine(fixture.ModelDirectory, "model.bim"), "{}");

        AssertError(fixture, "PBI-016");
    }

    [Fact]
    public void MissingTmdlModelFile_IsReported()
    {
        using var fixture = new ProjectFixture();
        File.Delete(Path.Combine(fixture.ModelDirectory, "definition", "model.tmdl"));

        AssertError(fixture, "PBI-017");
    }

    private void AssertError(ProjectFixture fixture, string expectedCode)
    {
        var result = _validator.Validate(fixture.PbipPath);
        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == expectedCode && diagnostic.Severity == DiagnosticSeverity.Error);
    }

    private sealed class ProjectFixture : IDisposable
    {
        public string Root { get; } = Path.Combine(
            Path.GetTempPath(), $"pbiq-validator-{Guid.NewGuid():N}");

        public string PbipPath => Path.Combine(Root, "FriendlyName.pbip");
        public string ReportDirectory => Path.Combine(Root, "Analytics.Report");
        public string ModelDirectory => Path.Combine(Root, "Finance.SemanticModel");
        public string PbirManifest => Path.Combine(ReportDirectory, "definition.pbir");
        public string PbismManifest => Path.Combine(ModelDirectory, "definition.pbism");

        public ProjectFixture()
        {
            Write(PbipPath, """
                {"version":"1.0","artifacts":[{"report":{"path":"Analytics.Report"}}]}
                """);
            Write(PbirManifest, """
                {"version":"4.0","datasetReference":{"byPath":{"path":"../Finance.SemanticModel"}}}
                """);
            Write(Path.Combine(ReportDirectory, "definition", "report.json"), "{}");
            Write(Path.Combine(ReportDirectory, "definition", "version.json"), """{"version":"4.0"}""");
            Write(Path.Combine(ReportDirectory, "definition", "pages", "Page1", "page.json"), "{}");
            Write(PbismManifest, """{"version":"4.0"}""");
            Write(Path.Combine(ModelDirectory, "definition", "model.tmdl"), "model Model");
            Write(Path.Combine(ModelDirectory, "definition", "database.tmdl"), "database Database");
        }

        public void Write(string path, string content)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}

using System.Text.Json;

using PowerBIQualityGate.Cli.Commands;
using PowerBIQualityGate.Core.Discovery;

using Spectre.Console.Testing;

namespace PowerBIQualityGate.Tests;

public sealed class FabricGitAuditTests
{
    [Fact]
    public void ExplicitFabricItemProducesJsonWithVerifiedSourceLine()
    {
        var (exit, root, stderr) = AuditJson(FabricItem());
        Assert.Equal(0, exit);
        Assert.Equal(string.Empty, stderr);
        Assert.Equal("1.0.0", root.GetProperty("schemaVersion").GetString());
        Assert.Equal("Revenue.SemanticModel", root.GetProperty("project").GetProperty("name").GetString());
        Assert.Equal(2, root.GetProperty("project").GetProperty("tables").GetInt32());
        Assert.Equal(4, root.GetProperty("summary").GetProperty("rulesEvaluated").GetInt32());
        var finding = Assert.Single(root.GetProperty("diagnostics").EnumerateArray(),
            item => item.GetProperty("location").GetProperty("objectPath").GetString() == "Sales[ProductId]");
        var location = finding.GetProperty("location");
        Assert.Equal("Revenue.SemanticModel/definition/tables/Sales.tmdl",
            location.GetProperty("filePath").GetString());
        Assert.Equal(8, location.GetProperty("line").GetInt32());
    }

    [Fact]
    public void SingleItemWorkspaceWithoutPbipAutoDiscoversFabric()
    {
        var result = new AuditInputDiscovery().Discover(FabricWorkspace());
        Assert.True(result.IsSuccess);
        Assert.Equal(AuditInputKind.FabricSemanticModel, result.Kind);
        Assert.EndsWith("Revenue.SemanticModel", result.EntryPath!, StringComparison.OrdinalIgnoreCase);
        var (exit, root, stderr) = AuditJson(FabricWorkspace());
        Assert.Equal(0, exit);
        Assert.Equal("Revenue.SemanticModel", root.GetProperty("project").GetProperty("name").GetString());
        Assert.Equal(string.Empty, stderr);
    }

    [Fact]
    public void FabricItemWithTrailingDirectorySeparatorStillMapsSourceCorrectly()
    {
        var withSeparator = FabricItem() + Path.DirectorySeparatorChar;
        var (exit, root, _) = AuditJson(withSeparator);
        Assert.Equal(0, exit);
        Assert.Equal("Revenue.SemanticModel", root.GetProperty("project").GetProperty("name").GetString());
        var finding = Assert.Single(root.GetProperty("diagnostics").EnumerateArray(),
            item => item.GetProperty("location").GetProperty("objectPath").GetString() == "Sales[ProductId]");
        Assert.Equal("Revenue.SemanticModel/definition/tables/Sales.tmdl",
            finding.GetProperty("location").GetProperty("filePath").GetString());
    }

    [Fact]
    public void ExistingPbipProjectStillTakesPrecedence()
    {
        var discovery = new AuditInputDiscovery().Discover(Path.Combine(SolutionRoot(), "examples", "valid-pbir-tmdl"));
        Assert.True(discovery.IsSuccess);
        Assert.Equal(AuditInputKind.Pbip, discovery.Kind);
        Assert.EndsWith("Example.pbip", discovery.EntryPath!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FabricSarifUrisAreRelativeToCheckoutRoot()
    {
        using var output = new TestConsole();
        using var error = new TestConsole();
        using var writer = new StringWriter();
        var exit = AuditCommand.Execute(FabricItem(), output, error,
            sarifOutput: writer, sarifSourceRoot: SolutionRoot());
        using var document = JsonDocument.Parse(writer.ToString());
        var run = document.RootElement.GetProperty("runs")[0];
        Assert.Equal(0, exit);
        Assert.Equal(string.Empty, error.Output);
        var finding = Assert.Single(run.GetProperty("results").EnumerateArray(),
            item => item.GetProperty("properties").GetProperty("objectPath").GetString() == "Sales[ProductId]");
        var coordinate = finding.GetProperty("locations")[0].GetProperty("physicalLocation");
        Assert.Equal("examples/fabric-git/Revenue.SemanticModel/definition/tables/Sales.tmdl",
            coordinate.GetProperty("artifactLocation").GetProperty("uri").GetString());
        Assert.Equal(8, coordinate.GetProperty("region").GetProperty("startLine").GetInt32());
    }

    [Fact]
    public void FabricDefaultConfigIsReadFromWorkspaceBesideItem()
    {
        using var workspace = new FabricTempWorkspace();
        File.WriteAllText(Path.Combine(workspace.Path, ".pbiq.yml"), """
            version: 1
            exclusions:
              - rule: SM-003
                object: Sales[ProductId]
            """);
        var (exit, root, stderr) = AuditJson(workspace.ModelPath);
        Assert.Equal(0, exit);
        Assert.Equal(1, root.GetProperty("summary").GetProperty("suppressed").GetInt32());
        Assert.Equal(2, root.GetProperty("summary").GetProperty("information").GetInt32());
        Assert.Equal(string.Empty, stderr);
    }

    [Fact]
    public void MultipleFabricItemsRequireAnExplicitDirectory()
    {
        using var workspace = new FabricTempWorkspace();
        Directory.CreateDirectory(Path.Combine(workspace.Path, "Second.SemanticModel"));
        var discovery = new AuditInputDiscovery().Discover(workspace.Path);
        Assert.False(discovery.IsSuccess);
        Assert.Equal(DiscoveryStatus.MultipleProjects, discovery.Status);
        var (exit, root, _) = AuditJson(workspace.Path);
        Assert.Equal(2, exit);
        Assert.Equal("PROJECT_DISCOVERY_FAILED", root.GetProperty("failure").GetProperty("code").GetString());
    }

    [Fact]
    public void FabricWarningHonorsFailOnWarningAndKeepsSarifLocation()
    {
        using var output = new TestConsole();
        using var error = new TestConsole();
        using var writer = new StringWriter();
        var input = Path.Combine(SolutionRoot(), "examples", "fabric-git-warning", "Warning.SemanticModel");
        var exit = AuditCommand.Execute(input, output, error,
            failOnWarning: true, sarifOutput: writer, sarifSourceRoot: SolutionRoot());
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Equal(1, exit);
        var run = document.RootElement.GetProperty("runs")[0];
        Assert.Equal(1, run.GetProperty("properties").GetProperty("pbiqExitCode").GetInt32());
        var warning = Assert.Single(run.GetProperty("results").EnumerateArray(),
            item => item.GetProperty("ruleId").GetString() == "SM-001");
        Assert.Equal("warning", warning.GetProperty("level").GetString());
        Assert.Equal("examples/fabric-git-warning/Warning.SemanticModel/definition/relationships.tmdl",
            warning.GetProperty("locations")[0].GetProperty("physicalLocation")
                .GetProperty("artifactLocation").GetProperty("uri").GetString());
        Assert.Equal(string.Empty, error.Output);
    }

    [Fact]
    public void InvalidFabricPbismDoesNotReportPass()
    {
        using var workspace = new FabricTempWorkspace();
        File.WriteAllText(Path.Combine(workspace.ModelPath, "definition.pbism"), "not-json");
        var (exit, root, _) = AuditJson(workspace.ModelPath);
        Assert.Equal(1, exit);
        Assert.Equal("STRUCTURE_VALIDATION_FAILED", root.GetProperty("failure").GetProperty("code").GetString());
        Assert.Equal("not_evaluated", root.GetProperty("result").GetProperty("status").GetString());
    }

    [Fact]
    public void IncompleteFabricTmdlDoesNotReportPass()
    {
        using var workspace = new FabricTempWorkspace();
        File.Delete(Path.Combine(workspace.ModelPath, "definition", "database.tmdl"));
        var (exit, root, _) = AuditJson(workspace.ModelPath);
        Assert.Equal(1, exit);
        Assert.Equal("STRUCTURE_VALIDATION_FAILED", root.GetProperty("failure").GetProperty("code").GetString());
    }

    [Fact]
    public void FabricTmslIsExplicitlyUnsupported()
    {
        using var workspace = new FabricTempWorkspace();
        Directory.Delete(Path.Combine(workspace.ModelPath, "definition"), recursive: true);
        File.WriteAllText(Path.Combine(workspace.ModelPath, "model.bim"), "{}");
        var (exit, root, _) = AuditJson(workspace.ModelPath);
        Assert.Equal(2, exit);
        Assert.Equal("LOCAL_TMDL_REQUIRED", root.GetProperty("failure").GetProperty("code").GetString());
    }

    [Fact]
    public void FabricConflictingModelFormatsFailStructuralValidation()
    {
        using var workspace = new FabricTempWorkspace();
        File.WriteAllText(Path.Combine(workspace.ModelPath, "model.bim"), "{}");
        var (exit, root, _) = AuditJson(workspace.ModelPath);
        Assert.Equal(1, exit);
        Assert.Equal("STRUCTURE_VALIDATION_FAILED", root.GetProperty("failure").GetProperty("code").GetString());
    }

    [Fact]
    public void MissingFabricManifestFailsStructuralValidation()
    {
        using var workspace = new FabricTempWorkspace();
        File.Delete(Path.Combine(workspace.ModelPath, "definition.pbism"));
        var (exit, root, _) = AuditJson(workspace.ModelPath);
        Assert.Equal(1, exit);
        Assert.Equal("STRUCTURE_VALIDATION_FAILED", root.GetProperty("failure").GetProperty("code").GetString());
    }

    [Fact]
    public void UnrelatedDirectoryIsNotAssumedToBeFabric()
    {
        using var workspace = new FabricTempWorkspace();
        var unrelated = Directory.CreateDirectory(Path.Combine(workspace.Path, "Other.Report"));
        var (exit, root, _) = AuditJson(unrelated.FullName);
        Assert.Equal(2, exit);
        Assert.Equal("PROJECT_DISCOVERY_FAILED", root.GetProperty("failure").GetProperty("code").GetString());
    }

    private static (int Exit, JsonElement Root, string Error) AuditJson(string path)
    {
        using var output = new TestConsole();
        using var error = new TestConsole();
        using var writer = new StringWriter();
        var exit = AuditCommand.Execute(path, output, error, jsonOutput: writer);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(string.Empty, output.Output);
        return (exit, doc.RootElement.Clone(), error.Output);
    }

    private static string FabricWorkspace() => Path.Combine(SolutionRoot(), "examples", "fabric-git");
    private static string FabricItem() => Path.Combine(FabricWorkspace(), "Revenue.SemanticModel");

    private static string SolutionRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PowerBIQualityGate.slnx")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Solution root not found.");
    }

    private sealed class FabricTempWorkspace : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "pbiq-fabric-" + Guid.NewGuid().ToString("N"));
        public string ModelPath => System.IO.Path.Combine(Path, "Revenue.SemanticModel");

        public FabricTempWorkspace()
        {
            foreach (var source in Directory.EnumerateFiles(FabricItem(), "*", SearchOption.AllDirectories))
            {
                var relative = System.IO.Path.GetRelativePath(FabricItem(), source);
                var target = System.IO.Path.Combine(ModelPath, relative);
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
                File.Copy(source, target);
            }
        }

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}

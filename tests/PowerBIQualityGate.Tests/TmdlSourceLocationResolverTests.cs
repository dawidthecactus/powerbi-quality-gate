using PowerBIQualityGate.Core.Inspection;
using PowerBIQualityGate.Core.Quality;

namespace PowerBIQualityGate.Tests;

public sealed class TmdlSourceLocationResolverTests
{
    [Fact]
    public void ValidFixtureMapsColumnToPhysicalDeclaration()
    {
        var root = FindRepositoryRoot();
        var project = Path.Combine(root, "examples", "valid-pbir-tmdl");
        var definition = Path.Combine(project, "Example.SemanticModel", "definition");
        var model = new TomModelReader().Read(definition);
        var resolver = new TmdlSourceLocationResolver(definition, project);

        var source = resolver.Resolve("SM-003", "Sales[ProductId]", model);

        Assert.NotNull(source);
        Assert.Equal("Example.SemanticModel/definition/tables/Sales.tmdl", source.FilePath);
        Assert.Equal(8, source.Line);
        Assert.DoesNotContain('\\', source.FilePath);
    }

    [Fact]
    public void WarningFixtureMapsRelationshipToPhysicalDeclaration()
    {
        var root = FindRepositoryRoot();
        var project = Path.Combine(root, "examples", "warning-pbir-tmdl");
        var definition = Path.Combine(project, "Example.SemanticModel", "definition");
        var model = new TomModelReader().Read(definition);
        var audit = new ModelQualityAuditEngine(ModelQualityRulePack.CreateDefault()).Run(model);
        var finding = Assert.Single(audit.Findings, item => item.RuleCode == "SM-001");
        var resolver = new TmdlSourceLocationResolver(definition, project);

        var source = resolver.Resolve(finding.RuleCode, finding.Location, model);

        Assert.NotNull(source);
        Assert.Equal("Example.SemanticModel/definition/relationships.tmdl", source.FilePath);
        Assert.Equal(1, source.Line);
    }

    [Fact]
    public void QuotedMeasureAndNestedDaxTextDoNotCreateGhostLocations()
    {
        var root = CreateTempProject();
        try
        {
            var definition = Path.Combine(root, "Example.SemanticModel", "definition");
            var tables = Path.Combine(definition, "tables");
            Directory.CreateDirectory(tables);
            File.WriteAllLines(Path.Combine(tables, "Sales.tmdl"),
            [
                "table 'Sales Data'",
                "\tmeasure 'O''Brien Count' =",
                "\t\tVAR X = 1",
                "\t\tmeasure Ghost = 4",
                "\t\tRETURN X",
                "\tcolumn CustomerId",
                "\t\tdataType: int64"
            ]);
            var model = new TomModelSnapshot("Test", 1600,
            [
                new TomTableSnapshot("Sales Data", false,
                [new TomColumnSnapshot("CustomerId", "Int64", "Data", false, null)],
                [
                    new TomMeasureSnapshot("O'Brien Count", "1", null, false),
                    new TomMeasureSnapshot("Ghost", "4", null, false)
                ], 0, 0)
            ], []);
            var resolver = new TmdlSourceLocationResolver(definition, root);

            Assert.Equal(2, resolver.Resolve("SM-002", "Sales Data[O'Brien Count]", model)?.Line);
            Assert.Equal(6, resolver.Resolve("SM-003", "Sales Data[CustomerId]", model)?.Line);
            Assert.Null(resolver.Resolve("SM-002", "Sales Data[Ghost]", model));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void DuplicateDeclarationsFailClosedRatherThanChoosingAFile()
    {
        var root = CreateTempProject();
        try
        {
            var definition = Path.Combine(root, "Example.SemanticModel", "definition");
            var tables = Path.Combine(definition, "tables");
            Directory.CreateDirectory(tables);
            File.WriteAllText(Path.Combine(tables, "One.tmdl"), "table Sales\n\tcolumn ProductId\n");
            File.WriteAllText(Path.Combine(tables, "Two.tmdl"), "table Sales\n\tcolumn ProductId\n");
            var model = new TomModelSnapshot("Test", 1600,
            [
                new TomTableSnapshot("Sales", false,
                    [new TomColumnSnapshot("ProductId", "Int64", "Data", false, null)],
                    [], 0, 0)
            ], []);

            Assert.Null(new TmdlSourceLocationResolver(definition, root)
                .Resolve("SM-003", "Sales[ProductId]", model));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SourceOutsideProjectDoesNotLeakAbsoluteOrParentPath()
    {
        var root = CreateTempProject();
        try
        {
            var project = Path.Combine(root, "AnotherProject");
            Directory.CreateDirectory(project);
            var definition = Path.Combine(root, "Example.SemanticModel", "definition");
            var tables = Path.Combine(definition, "tables");
            Directory.CreateDirectory(tables);
            File.WriteAllText(Path.Combine(tables, "Sales.tmdl"), "table Sales\n\tcolumn ProductId\n");
            var model = new TomModelSnapshot("Test", 1600,
            [
                new TomTableSnapshot("Sales", false,
                    [new TomColumnSnapshot("ProductId", "Int64", "Data", false, null)],
                    [], 0, 0)
            ], []);

            Assert.Null(new TmdlSourceLocationResolver(definition, project)
                .Resolve("SM-003", "Sales[ProductId]", model));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTempProject()
    {
        var path = Path.Combine(Path.GetTempPath(), "pbiq-source-map-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string FindRepositoryRoot()
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
        throw new DirectoryNotFoundException("Could not locate solution root.");
    }
}

using PowerBIQualityGate.Core.Inspection;
using PowerBIQualityGate.Core.Quality;
using PowerBIQualityGate.Core.Quality.Rules;
using PowerBIQualityGate.Core.Validation;

namespace PowerBIQualityGate.Tests;

public sealed class ModelQualityRulePackTests
{
    [Fact]
    public void DefaultCatalog_RegistersFourStableUniqueRules()
    {
        var rules = ModelQualityRulePack.CreateDefault();

        Assert.Equal(new[] { "SM-001", "SM-002", "SM-003", "SM-004" },
            rules.Select(rule => rule.Code));
        Assert.Equal(new[]
        {
            DiagnosticSeverity.Warning,
            DiagnosticSeverity.Warning,
            DiagnosticSeverity.Information,
            DiagnosticSeverity.Information
        }, rules.Select(rule => rule.Severity));
        Assert.All(rules, rule => Assert.False(string.IsNullOrWhiteSpace(rule.Title)));
        Assert.Equal(4, rules.Select(rule => rule.Code).Distinct().Count());
    }

    [Fact]
    public void DefaultCatalog_IsReadOnlyAndReturnsIndependentInstances()
    {
        var first = ModelQualityRulePack.CreateDefault();
        var second = ModelQualityRulePack.CreateDefault();

        Assert.IsAssignableFrom<System.Collections.ObjectModel.ReadOnlyCollection<IModelQualityRule>>(first);
        Assert.NotSame(first, second);
        Assert.NotSame(first[0], second[0]);
    }

    [Fact]
    public void EmptyModel_EvaluatesAllFourRulesAndPasses()
    {
        var result = Run(Model());

        Assert.Equal(4, result.RulesEvaluated);
        Assert.Equal(QualityAuditVerdict.Passed, result.Verdict);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void RulePack_DoesNotModifySnapshotsOrAccumulateFindings()
    {
        var model = Model(
            [Table("Sales", columns: [Column("CustomerId")])],
            [Relationship("r1", active: false)]);
        var engine = new ModelQualityAuditEngine(ModelQualityRulePack.CreateDefault());

        var first = engine.Run(model);
        var second = engine.Run(model);

        Assert.Equal(2, first.Findings.Count);
        Assert.Equal(first.Findings.ToArray(), second.Findings.ToArray());
        Assert.False(model.Tables[0].Columns[0].IsHidden);
        Assert.False(model.Relationships[0].IsActive);
    }

    [Fact]
    public void Bidirectional_ActiveRelationshipProducesWarning()
    {
        var result = Run(Model(relationships: [Relationship("r1", direction: "BothDirections")]));

        var finding = Assert.Single(result.Findings);
        Assert.Equal("SM-001", finding.RuleCode);
        Assert.Equal(DiagnosticSeverity.Warning, finding.Severity);
        Assert.Contains("Sales[ProductId]", finding.Location);
        Assert.Contains("Product[ProductId]", finding.Location);
        Assert.Contains("r1", finding.Location);
        Assert.Contains("single-direction", finding.HowToFix);
        Assert.Equal(QualityAuditVerdict.Passed, result.Verdict);
    }

    [Theory]
    [InlineData("OneDirection")]
    [InlineData("Automatic")]
    [InlineData("Unknown")]
    public void Bidirectional_OtherFilterDirectionsAreNotFlagged(string direction)
    {
        var model = Model(relationships: [Relationship("r1", direction: direction)]);

        Assert.Empty(new BidirectionalRelationshipRule().Evaluate(model));
    }

    [Fact]
    public void Bidirectional_InactiveRelationshipIsHandledBySm004Instead()
    {
        var model = Model(relationships: [Relationship("r1", active: false, direction: "BothDirections")]);
        var result = Run(model);

        Assert.Single(result.Findings);
        Assert.Equal("SM-004", result.Findings[0].RuleCode);
    }

    [Fact]
    public void Bidirectional_FilterDirectionMatchingIsCaseInsensitive()
    {
        var model = Model(relationships: [Relationship("r1", direction: "bothdirections")]);

        Assert.Single(new BidirectionalRelationshipRule().Evaluate(model));
    }

    [Fact]
    public void Bidirectional_MultipleRelationshipsGetSeparateLocations()
    {
        var model = Model(relationships:
        [
            Relationship("first", direction: "BothDirections"),
            Relationship("second", direction: "BothDirections")
        ]);

        var findings = Run(model).Findings;
        Assert.Equal(2, findings.Count);
        Assert.Contains(findings, finding => finding.Location.Contains("first", StringComparison.Ordinal));
        Assert.Contains(findings, finding => finding.Location.Contains("second", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Int64")]
    [InlineData("Decimal")]
    [InlineData("Double")]
    [InlineData("decimal")]
    public void MeasureFormatting_VisibleUnformattedNumericMeasureIsWarning(string type)
    {
        var model = Model([Table("Sales", measures: [Measure("Revenue", dataType: type)])]);
        var finding = Assert.Single(new UnformattedNumericMeasureRule().Evaluate(model));

        Assert.Equal("Sales[Revenue]", finding.Location);
        Assert.Contains("format", finding.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("format", finding.HowToFix, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("String")]
    [InlineData("Boolean")]
    [InlineData("DateTime")]
    [InlineData("Variant")]
    [InlineData("Unknown")]
    [InlineData(null)]
    public void MeasureFormatting_NonNumericOrUnknownTypesAreNotGuessed(string? dataType)
    {
        var model = Model([Table("Sales", measures: [Measure("Revenue", dataType: dataType ?? "Unknown")])]);

        Assert.Empty(new UnformattedNumericMeasureRule().Evaluate(model));
    }

    [Theory]
    [InlineData("#,##0")]
    [InlineData("0.00")]
    [InlineData("0.0%")]
    public void MeasureFormatting_StaticFormatPreventsFinding(string format)
    {
        var model = Model([Table("Sales", measures: [Measure("Revenue", format: format)])]);

        Assert.Empty(new UnformattedNumericMeasureRule().Evaluate(model));
    }

    [Fact]
    public void MeasureFormatting_DynamicFormatPreventsFinding()
    {
        var model = Model([Table("Sales", measures: [Measure("Revenue", dynamicFormat: true)])]);

        Assert.Empty(new UnformattedNumericMeasureRule().Evaluate(model));
    }

    [Fact]
    public void MeasureFormatting_HiddenMeasureIsIgnored()
    {
        var model = Model([Table("Sales", measures: [Measure("Revenue", hidden: true)])]);

        Assert.Empty(new UnformattedNumericMeasureRule().Evaluate(model));
    }

    [Fact]
    public void MeasureFormatting_HiddenTableIsIgnored()
    {
        var model = Model([Table("Sales", hidden: true, measures: [Measure("Revenue")])]);

        Assert.Empty(new UnformattedNumericMeasureRule().Evaluate(model));
    }

    [Fact]
    public void MeasureFormatting_WhitespaceOnlyFormatIsTreatedAsMissing()
    {
        var model = Model([Table("Sales", measures: [Measure("Revenue", format: "  ")])]);

        Assert.Single(new UnformattedNumericMeasureRule().Evaluate(model));
    }

    [Theory]
    [InlineData("ID")]
    [InlineData("Key")]
    [InlineData("GUID")]
    [InlineData("ProductId")]
    [InlineData("OrderID")]
    [InlineData("CustomerKey")]
    [InlineData("Order ID")]
    [InlineData("Product_Key")]
    [InlineData("product_id")]
    [InlineData("FK_Product")]
    [InlineData("PK_Customer")]
    public void TechnicalColumn_ConventionalIdentifiersProduceInfo(string name)
    {
        var model = Model([Table("Sales", columns: [Column(name)])]);
        var finding = Assert.Single(new VisibleTechnicalColumnRule().Evaluate(model));

        Assert.Equal($"Sales[{name}]", finding.Location);
        Assert.Contains("hiding", finding.HowToFix, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("SM-003", Run(model).Findings[0].RuleCode);
    }

    [Theory]
    [InlineData("Paid")]
    [InlineData("Grid")]
    [InlineData("Holiday")]
    [InlineData("Identifier")]
    [InlineData("Product")]
    [InlineData("Customer Name")]
    [InlineData("PrimaryKeyLabel")]
    public void TechnicalColumn_OrdinaryNamesAreNotFlagged(string name)
    {
        var model = Model([Table("Sales", columns: [Column(name)])]);

        Assert.Empty(new VisibleTechnicalColumnRule().Evaluate(model));
    }

    [Fact]
    public void TechnicalColumn_HiddenColumnIsIgnored()
    {
        var model = Model([Table("Sales", columns: [Column("ProductId", hidden: true)])]);

        Assert.Empty(new VisibleTechnicalColumnRule().Evaluate(model));
    }

    [Fact]
    public void TechnicalColumn_HiddenTableIsIgnored()
    {
        var model = Model([Table("Sales", hidden: true, columns: [Column("ProductId")])]);

        Assert.Empty(new VisibleTechnicalColumnRule().Evaluate(model));
    }

    [Fact]
    public void InactiveRelationship_ReportsInfoAndRemediation()
    {
        var model = Model(relationships: [Relationship("alternateDate", active: false)]);
        var finding = Assert.Single(new InactiveRelationshipRule().Evaluate(model));

        Assert.Contains("alternateDate", finding.Location);
        Assert.Contains("USERELATIONSHIP", finding.HowToFix);
        Assert.Contains("intentional", finding.WhyItMatters, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("SM-004", Run(model).Findings[0].RuleCode);
        Assert.Equal(QualityAuditVerdict.Passed, Run(model).Verdict);
    }

    [Fact]
    public void InactiveRelationship_ActiveRelationshipIsIgnored()
    {
        var model = Model(relationships: [Relationship("active")]);

        Assert.Empty(new InactiveRelationshipRule().Evaluate(model));
    }

    [Fact]
    public void AllFourRules_RunTogetherWithStableSeverities()
    {
        var model = Model(
            [Table("Sales", columns: [Column("CustomerId")], measures: [Measure("Revenue")])],
            [Relationship("both", direction: "BothDirections"), Relationship("inactive", active: false)]);

        var result = Run(model);

        Assert.Equal(new[] { "SM-001", "SM-002", "SM-003", "SM-004" },
            result.Findings.Select(finding => finding.RuleCode));
        Assert.Equal(2, result.WarningCount);
        Assert.Equal(2, result.InformationCount);
        Assert.Equal(0, result.ErrorCount);
        Assert.Equal(QualityAuditVerdict.Passed, result.Verdict);
    }

    [Fact]
    public void IssuesHaveCompleteGuidanceAndDoNotModifySource()
    {
        var model = Model(
            [Table("Sales", columns: [Column("CustomerId")], measures: [Measure("Revenue")])],
            [Relationship("both", direction: "BothDirections"), Relationship("inactive", active: false)]);

        var result = Run(model);

        Assert.All(result.Findings, finding =>
        {
            Assert.False(string.IsNullOrWhiteSpace(finding.RuleTitle));
            Assert.False(string.IsNullOrWhiteSpace(finding.Location));
            Assert.False(string.IsNullOrWhiteSpace(finding.Message));
            Assert.False(string.IsNullOrWhiteSpace(finding.WhyItMatters));
            Assert.False(string.IsNullOrWhiteSpace(finding.HowToFix));
        });
        Assert.Null(model.Tables[0].Measures[0].FormatString);
        Assert.False(model.Tables[0].Columns[0].IsHidden);
    }

    private static ModelQualityAuditResult Run(TomModelSnapshot model) =>
        new ModelQualityAuditEngine(ModelQualityRulePack.CreateDefault()).Run(model);

    private static TomModelSnapshot Model(
        IReadOnlyList<TomTableSnapshot>? tables = null,
        IReadOnlyList<TomRelationshipSnapshot>? relationships = null) =>
        new("Demo", 1600, tables ?? [], relationships ?? []);

    private static TomTableSnapshot Table(
        string name,
        bool hidden = false,
        IReadOnlyList<TomColumnSnapshot>? columns = null,
        IReadOnlyList<TomMeasureSnapshot>? measures = null) =>
        new(name, hidden, columns ?? [], measures ?? [], 0, 0);

    private static TomColumnSnapshot Column(string name, bool hidden = false) =>
        new(name, "Int64", "Data", hidden, null);

    private static TomMeasureSnapshot Measure(
        string name,
        string? format = null,
        bool hidden = false,
        string dataType = "Decimal",
        bool dynamicFormat = false) =>
        new(name, "SUM(Sales[Amount])", format, hidden, dataType, dynamicFormat);

    private static TomRelationshipSnapshot Relationship(
        string name,
        bool active = true,
        string direction = "OneDirection") =>
        new(name, "Sales[ProductId]", "Product[ProductId]", active, direction);
}

using PowerBIQualityGate.Core.Inspection;

namespace PowerBIQualityGate.Tests;

public sealed class MeasureInspectionEngineTests
{
    [Fact]
    public void IndependentlyPartitionsFormatAndTypeForVisibleMeasures()
    {
        var model = Model(
        [
            Measure("KnownStatic", "Int64", "#,##0"),
            Measure("KnownDynamic", "Decimal", dynamicFormat: true, dynamicExpression: "\"0.0%\""),
            Measure("KnownMissing", "Double"),
            Measure("UnknownStatic", "Unknown", "0.00"),
            Measure("VariantDynamic", "Variant", dynamicFormat: true, dynamicExpression: "\"0.0\""),
            Measure("UnknownMissing", "Unknown"),
            Measure("Nonnumeric", "String"),
            Measure("Unsupported", "Currency"),
            Measure("BlankStatic", "Unknown", "   "),
            Measure("BlankDynamic", "Unknown", dynamicFormat: true, dynamicExpression: "   "),
            Measure("Both", "Unknown", "0.00", dynamicFormat: true, dynamicExpression: "\"#,0\""),
            Measure("LegacyDynamic", "Unknown", dynamicFormat: true),
            Measure("Hidden", "Double", "0.00", hidden: true)
        ], hiddenTableMeasures: [Measure("HiddenByTable", "Double")]);

        var report = new MeasureInspectionEngine().Inspect(model);
        var s = report.Summary;

        Assert.Equal(14, s.Total);
        Assert.Equal(12, s.Visible);
        Assert.Equal(2, s.Hidden);
        Assert.Equal(2, s.StaticFormats);
        Assert.Equal(5, s.DynamicFormats);
        Assert.Equal(5, s.MissingFormats);
        Assert.Equal(3, s.NumericTypes);
        Assert.Equal(1, s.NonNumericTypes);
        Assert.Equal(7, s.UnknownTypes);
        Assert.Equal(1, s.UnsupportedTypes);
        Assert.Equal(1, s.BlankStaticFormats);
        Assert.Equal(1, s.BlankDynamicExpressions);
        Assert.Equal(1, s.UnavailableDynamicExpressions);
        Assert.Equal(1, s.BothFormats);
        Assert.Equal(s.Visible, s.StaticFormats + s.DynamicFormats + s.MissingFormats);
        Assert.Equal(s.Visible, s.NumericTypes + s.NonNumericTypes + s.UnknownTypes + s.UnsupportedTypes);
        Assert.Equal(s.Total, report.Measures.Count);
    }

    [Fact]
    public void UnknownTypeWithStaticFormatIsStillInspected()
    {
        var entry = Assert.Single(new MeasureInspectionEngine().Inspect(
            Model([Measure("Ambiguous", "Unknown", "0.00")])).Measures);

        Assert.Equal(MeasureFormatKind.Static, entry.FormatKind);
        Assert.Equal(MeasureDataTypeKind.Unknown, entry.DataTypeKind);
    }

    [Fact]
    public void DynamicFormatTakesPrecedenceAndReportsBothDefinitions()
    {
        var entry = Assert.Single(new MeasureInspectionEngine().Inspect(Model(
            [Measure("Both", "Unknown", "0.0", dynamicFormat: true, dynamicExpression: "\"0.0%\"")])).Measures);

        Assert.Equal(MeasureFormatKind.Dynamic, entry.FormatKind);
        Assert.Equal(DynamicFormatExpressionKind.Present, entry.DynamicExpressionKind);
        Assert.True(entry.HasBothFormats);
    }

    [Theory]
    [InlineData("Int64")]
    [InlineData("Decimal")]
    [InlineData("Double")]
    [InlineData(" DECIMAL ")]
    public void KnownNumericTypesAreCaseInsensitiveAndTrimmed(string dataType)
    {
        var entry = Assert.Single(new MeasureInspectionEngine().Inspect(Model(
            [Measure("Revenue", dataType)])).Measures);

        Assert.Equal(MeasureDataTypeKind.Numeric, entry.DataTypeKind);
    }

    [Theory]
    [InlineData("String")]
    [InlineData("Boolean")]
    [InlineData("DateTime")]
    [InlineData("Binary")]
    public void KnownNonNumericTypesAreDistinguished(string dataType)
    {
        var entry = Assert.Single(new MeasureInspectionEngine().Inspect(Model(
            [Measure("Value", dataType)])).Measures);

        Assert.Equal(MeasureDataTypeKind.NonNumeric, entry.DataTypeKind);
    }

    [Theory]
    [InlineData("Unknown")]
    [InlineData("Variant")]
    [InlineData("")]
    [InlineData("   ")]
    public void UnknownAndVariantTypesRemainExplicitlyUnknown(string dataType)
    {
        var entry = Assert.Single(new MeasureInspectionEngine().Inspect(Model(
            [Measure("Value", dataType)])).Measures);

        Assert.Equal(MeasureDataTypeKind.Unknown, entry.DataTypeKind);
    }

    [Fact]
    public void UnsupportedTypeIsNotMistakenForKnownNonNumeric()
    {
        var entry = Assert.Single(new MeasureInspectionEngine().Inspect(Model(
            [Measure("Value", "SomeFutureTOMType")])).Measures);

        Assert.Equal(MeasureDataTypeKind.Unsupported, entry.DataTypeKind);
    }

    [Fact]
    public void WhitespaceStaticFormatIsMissingAndFlagged()
    {
        var entry = Assert.Single(new MeasureInspectionEngine().Inspect(Model(
            [Measure("Value", "Unknown", "   ")])).Measures);

        Assert.Equal(MeasureFormatKind.Missing, entry.FormatKind);
        Assert.True(entry.HasBlankStaticFormat);
    }

    [Fact]
    public void BlankDynamicExpressionIsDistinguishedFromUnavailableExpression()
    {
        var report = new MeasureInspectionEngine().Inspect(Model(
        [
            Measure("Blank", "Unknown", dynamicFormat: true, dynamicExpression: "   "),
            Measure("Unavailable", "Unknown", dynamicFormat: true)
        ]));

        Assert.Equal(2, report.Summary.DynamicFormats);
        Assert.Equal(1, report.Summary.BlankDynamicExpressions);
        Assert.Equal(1, report.Summary.UnavailableDynamicExpressions);
        Assert.Equal(DynamicFormatExpressionKind.Blank, report.Measures[0].DynamicExpressionKind);
        Assert.Equal(DynamicFormatExpressionKind.Unavailable, report.Measures[1].DynamicExpressionKind);
    }

    [Fact]
    public void HiddenMeasuresAreInventoriedButExcludedFromVisiblePartitions()
    {
        var report = new MeasureInspectionEngine().Inspect(Model(
            [Measure("Private", "Unknown", hidden: true)],
            hiddenTableMeasures: [Measure("TablePrivate", "Unknown", "0.00")]));

        Assert.Equal(2, report.Summary.Total);
        Assert.Equal(2, report.Summary.Hidden);
        Assert.Equal(0, report.Summary.Visible);
        Assert.Equal(0, report.Summary.StaticFormats);
        Assert.All(report.Measures, entry => Assert.False(entry.IsVisible));
    }

    [Fact]
    public void Large763MeasureModelInspectsEveryFormatEvenWithUnknownTypes()
    {
        var measures = Enumerable.Range(1, 763).Select(i =>
            Measure($"SyntheticMeasure{i:D4}", "Unknown",
                i <= 300 ? "#,##0" : null,
                dynamicFormat: i > 300 && i <= 400,
                dynamicExpression: i > 300 && i <= 400 ? "\"0.0%\"" : null)).ToArray();
        var report = new MeasureInspectionEngine().Inspect(Model(measures));
        var s = report.Summary;

        Assert.Equal(763, s.Inspected);
        Assert.Equal(763, s.Visible);
        Assert.Equal(300, s.StaticFormats);
        Assert.Equal(100, s.DynamicFormats);
        Assert.Equal(363, s.MissingFormats);
        Assert.Equal(763, s.UnknownTypes);
        Assert.Equal(0, s.NumericTypes);
        Assert.Equal(0, s.BlankDynamicExpressions);
    }

    [Fact]
    public void InspectionDoesNotModifySnapshotOrLeakExpressionsIntoEntries()
    {
        var original = Measure("BusinessMetric", "Unknown", "#,##0");
        var model = Model([original]);
        var engine = new MeasureInspectionEngine();

        var first = engine.Inspect(model);
        var second = engine.Inspect(model);

        Assert.Equal(first.Summary, second.Summary);
        Assert.Equal(first.Measures.ToArray(), second.Measures.ToArray());
        Assert.Same(original, model.Tables[0].Measures[0]);
        Assert.Equal("#,##0", original.FormatString);
        Assert.DoesNotContain("SUM", first.Measures[0].ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EmptyModelHasZeroTotals()
    {
        var result = new MeasureInspectionEngine().Inspect(new TomModelSnapshot("Empty", 1600, [], []));

        Assert.Empty(result.Measures);
        Assert.Equal(0, result.Summary.Total);
        Assert.Equal(0, result.Summary.Visible);
    }

    [Fact]
    public void NullSnapshotThrows()
    {
        Assert.Throws<ArgumentNullException>(() => new MeasureInspectionEngine().Inspect(null!));
    }

    [Fact]
    public void ExistingSm002RulesAndGateRemainUnchangedForUnknownMeasures()
    {
        var model = Model([Measure("TextOrNumber", "Unknown", "#,##0")]);
        var inspection = new MeasureInspectionEngine().Inspect(model);
        var quality = new PowerBIQualityGate.Core.Quality.ModelQualityAuditEngine(
            PowerBIQualityGate.Core.Quality.ModelQualityRulePack.CreateDefault()).Run(model);
        var coverage = PowerBIQualityGate.Core.Quality.ModelAuditCoverageAnalyzer.Analyze(model, quality);
        var decision = new PowerBIQualityGate.Core.Quality.CoverageAwareGatePolicy(
            MinimumMeasureCoverage: 95).Evaluate(quality, coverage);

        Assert.Equal(1, inspection.Summary.StaticFormats);
        Assert.Equal(1, inspection.Summary.UnknownTypes);
        Assert.Equal(0, quality.WarningCount);
        Assert.Equal(0, coverage.MeasureFormatting!.CheckedNumeric);
        Assert.Equal(1, coverage.MeasureFormatting.UnknownType);
        Assert.Equal(1, decision.ExitCode);
    }

    private static TomMeasureSnapshot Measure(string name, string type, string? format = null,
        bool hidden = false, bool dynamicFormat = false, string? dynamicExpression = null) =>
        new(name, "SUM ( Fact[Value] )", format, hidden, type, dynamicFormat, dynamicExpression);

    private static TomModelSnapshot Model(IReadOnlyList<TomMeasureSnapshot> measures,
        IReadOnlyList<TomMeasureSnapshot>? hiddenTableMeasures = null)
    {
        var tables = new List<TomTableSnapshot>
        {
            new("Fact", false, [], measures, 0, 0)
        };
        if (hiddenTableMeasures is not null)
        {
            tables.Add(new TomTableSnapshot("Private", true, [], hiddenTableMeasures, 0, 0));
        }

        return new TomModelSnapshot("Synthetic", 1600, tables, []);
    }
}

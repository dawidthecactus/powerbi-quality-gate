using PowerBIQualityGate.Core.Inspection;
using PowerBIQualityGate.Core.Quality;

namespace PowerBIQualityGate.Tests;

public sealed class MeasureQualityConsistencyValidatorTests
{
    [Fact]
    public void MixedModelPartitionsAreConsistent()
    {
        var model = Model(
            new TomMeasureSnapshot("NumericMissing", "1", null, false, "Double"),
            new TomMeasureSnapshot("NumericStatic", "1", "#,0", false, "Int64"),
            new TomMeasureSnapshot("NumericDynamic", "1", null, false, "Decimal", true, "\"0.0\""),
            new TomMeasureSnapshot("Text", "\"abc\"", null, false, "String"),
            new TomMeasureSnapshot("UnknownMissing", "1", null, false, "Unknown"),
            new TomMeasureSnapshot("UnknownStatic", "1", "0.0", false, "Unknown"),
            new TomMeasureSnapshot("UnsupportedMissing", "1", null, false, "Custom"),
            new TomMeasureSnapshot("BadDynamic", "1", null, false, "Unknown", true, " "),
            new TomMeasureSnapshot("Hidden", "1", null, true, "Decimal"));
        var (audit, coverage) = Analyze(model);

        Assert.Empty(MeasureQualityConsistencyValidator.Validate(model, audit, coverage));
        Assert.Equal(1, coverage.Sm002Assessment!.ConfirmedWarnings);
        Assert.Equal(3, coverage.Sm002Assessment.NeedsReview);
    }

    [Fact]
    public void EmptyModelIsConsistent()
    {
        var model = new TomModelSnapshot("Empty", 1600, [], []);
        var (audit, coverage) = Analyze(model);

        Assert.Empty(MeasureQualityConsistencyValidator.Validate(model, audit, coverage));
    }

    [Fact]
    public void LargeSyntheticUnknownProfileKeepsTypeCoverageSeparate()
    {
        var model = Model(Enumerable.Range(0, 763)
            .Select(i => new TomMeasureSnapshot($"Metric{i}", "1",
                i < 104 ? "#,0" : null, false, "Unknown")).ToArray());
        var (audit, coverage) = Analyze(model);

        Assert.Empty(MeasureQualityConsistencyValidator.Validate(model, audit, coverage));
        Assert.Equal(763, coverage.Sm002Inspection!.Inspected);
        Assert.Equal(659, coverage.Sm002Assessment!.NeedsReview);
        Assert.Equal(0, coverage.MeasureFormatting!.CheckedNumeric);
    }

    [Fact]
    public void BlankStaticFallbackWithDynamicDefinitionIsStillConsistent()
    {
        var model = Model(new TomMeasureSnapshot("Dynamic", "1", " ", false,
            "Unknown", true, "\"0.00\""));
        var (audit, coverage) = Analyze(model);

        Assert.Equal(1, coverage.Sm002Inspection!.BlankStaticFormats);
        Assert.Equal(0, coverage.Sm002Inspection.MissingFormats);
        Assert.Equal(1, coverage.Sm002Inspection.DynamicFormats);
        Assert.Empty(MeasureQualityConsistencyValidator.Validate(model, audit, coverage));
    }

    [Fact]
    public void CorruptedFormatPartitionIsRejected()
    {
        var model = Model(new TomMeasureSnapshot("Revenue", "1", null, false));
        var (audit, coverage) = Analyze(model);
        var corrupted = coverage with
        {
            Sm002Inspection = coverage.Sm002Inspection! with { MissingFormats = 2 }
        };

        Assert.Contains("SM002_INSPECTION_PARTITION_INVALID",
            MeasureQualityConsistencyValidator.Validate(model, audit, corrupted));
    }

    [Fact]
    public void CorruptedDataTypePartitionIsRejected()
    {
        var model = Model(new TomMeasureSnapshot("Revenue", "1", null, false));
        var (audit, coverage) = Analyze(model);
        var corrupted = coverage with
        {
            Sm002Inspection = coverage.Sm002Inspection! with { UnknownTypes = 0 }
        };

        Assert.Contains("SM002_INSPECTION_PARTITION_INVALID",
            MeasureQualityConsistencyValidator.Validate(model, audit, corrupted));
    }

    [Fact]
    public void MissingSm002SummaryIsRejected()
    {
        var model = Model(new TomMeasureSnapshot("Revenue", "1", null, false));
        var (audit, coverage) = Analyze(model);

        Assert.Contains("SM002_SUMMARY_MISSING",
            MeasureQualityConsistencyValidator.Validate(model, audit,
                coverage with { Sm002Assessment = null }));
    }

    [Fact]
    public void WrongSnapshotTotalIsRejected()
    {
        var model = Model(new TomMeasureSnapshot("Revenue", "1", null, false));
        var (audit, coverage) = Analyze(model);
        var differentModel = Model(
            new TomMeasureSnapshot("Revenue", "1", null, false),
            new TomMeasureSnapshot("Another", "1", null, false));

        Assert.Contains("SM002_TOTAL_MISMATCH",
            MeasureQualityConsistencyValidator.Validate(differentModel, audit, coverage));
    }

    [Fact]
    public void VisibilityDisagreementIsRejected()
    {
        var model = Model(new TomMeasureSnapshot("Hidden", "1", null, true));
        var (audit, coverage) = Analyze(model);
        var corrupted = coverage with
        {
            Sm002Inspection = coverage.Sm002Inspection! with { Hidden = 0 }
        };

        Assert.Contains("SM002_VISIBILITY_MISMATCH",
            MeasureQualityConsistencyValidator.Validate(model, audit, corrupted));
    }

    [Fact]
    public void CorruptedMetadataNoteIsRejected()
    {
        var model = Model(new TomMeasureSnapshot("Revenue", "1", "0.0", false));
        var (audit, coverage) = Analyze(model);
        var corrupted = coverage with
        {
            Sm002Inspection = coverage.Sm002Inspection! with { BothFormats = 2 }
        };

        Assert.Contains("SM002_METADATA_NOTES_INVALID",
            MeasureQualityConsistencyValidator.Validate(model, audit, corrupted));
    }

    [Fact]
    public void CorruptedLegacyCoverageIsRejected()
    {
        var model = Model(new TomMeasureSnapshot("Revenue", "1", null, false));
        var (audit, coverage) = Analyze(model);
        var corrupted = coverage with
        {
            MeasureFormatting = coverage.MeasureFormatting! with { UnknownType = 0 }
        };

        Assert.Contains("SM002_LEGACY_COVERAGE_MISMATCH",
            MeasureQualityConsistencyValidator.Validate(model, audit, corrupted));
    }

    [Fact]
    public void CorruptedAssessmentIsRejected()
    {
        var model = Model(new TomMeasureSnapshot("Revenue", "1", null, false));
        var (audit, coverage) = Analyze(model);
        var corrupted = coverage with
        {
            Sm002Assessment = coverage.Sm002Assessment! with { UnknownTypeMissingFormat = 0 }
        };

        Assert.Contains("SM002_ASSESSMENT_PARTITION_INVALID",
            MeasureQualityConsistencyValidator.Validate(model, audit, corrupted));
    }

    [Fact]
    public void ReportedWarningsMustMatchSm002Findings()
    {
        var model = Model(new TomMeasureSnapshot("Revenue", "1", null, false, "Double"));
        var (audit, coverage) = Analyze(model);
        var alteredAudit = audit with { Findings = [] };

        Assert.Contains("SM002_FINDING_COUNT_MISMATCH",
            MeasureQualityConsistencyValidator.Validate(model, alteredAudit, coverage));
    }

    [Fact]
    public void MissingRuleDoesNotRequireSm002Data()
    {
        var model = Model(new TomMeasureSnapshot("Revenue", "1", null, false));
        var audit = new ModelQualityAuditResult("Synthetic", ["SM-003"], []);
        var coverage = ModelAuditCoverageAnalyzer.Analyze(model, audit);

        Assert.Empty(MeasureQualityConsistencyValidator.Validate(model, audit, coverage));
    }

    [Fact]
    public void UnevaluatedSm002CannotClaimItsCoverage()
    {
        var model = Model(new TomMeasureSnapshot("Revenue", "1", null, false));
        var (audit, coverage) = Analyze(model);
        var unevaluated = audit with { EvaluatedRuleCodes = ["SM-003"] };

        Assert.Contains("SM002_UNEVALUATED_COVERAGE",
            MeasureQualityConsistencyValidator.Validate(model, unevaluated, coverage));
    }

    [Fact]
    public void ValidatorRejectsNullArguments()
    {
        var model = Model();
        var (audit, coverage) = Analyze(model);

        Assert.Throws<ArgumentNullException>(() => MeasureQualityConsistencyValidator.Validate(null!, audit, coverage));
        Assert.Throws<ArgumentNullException>(() => MeasureQualityConsistencyValidator.Validate(model, null!, coverage));
        Assert.Throws<ArgumentNullException>(() => MeasureQualityConsistencyValidator.Validate(model, audit, null!));
    }

    private static TomModelSnapshot Model(params TomMeasureSnapshot[] measures) => new(
        "Synthetic", 1600, [new TomTableSnapshot("Metrics", false, [], measures, 0, 1)], []);

    private static (ModelQualityAuditResult Audit, ModelAuditCoverage Coverage) Analyze(TomModelSnapshot model)
    {
        var audit = new ModelQualityAuditEngine(ModelQualityRulePack.CreateDefault()).Run(model);
        return (audit, ModelAuditCoverageAnalyzer.Analyze(model, audit));
    }
}

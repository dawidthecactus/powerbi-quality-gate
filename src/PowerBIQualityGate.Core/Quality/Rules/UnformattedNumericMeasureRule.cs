using PowerBIQualityGate.Core.Inspection;
using PowerBIQualityGate.Core.Quality;
using PowerBIQualityGate.Core.Validation;

namespace PowerBIQualityGate.Core.Quality.Rules;

/// <summary>
/// SM-002 warns only when numeric type and missing model-level formatting are
/// both confirmed by metadata. Unknown types and unverified dynamic format
/// definitions are collected for review, never treated as warnings.
/// </summary>
public sealed class UnformattedNumericMeasureRule : IModelQualityRule
{
    public string Code => "SM-002";
    public string Title => "Format visible numeric measures";
    public DiagnosticSeverity Severity => DiagnosticSeverity.Warning;

    public IReadOnlyList<ModelQualityIssue> Evaluate(TomModelSnapshot model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var assessment = new MeasureFormattingAssessmentEngine().Assess(model);
        return assessment.Measures
            .Where(item => item.Kind == MeasureFormattingAssessmentKind.KnownNumericMissingFormat)
            .Select(item => new ModelQualityIssue(
                $"{item.TableName}[{item.MeasureName}]",
                $"Numeric measure '{item.MeasureName}' has no model-level format string.",
                "Without an explicit format, numbers can display inconsistently across reports and visuals.",
                "Set an appropriate format string or a dynamic format-string expression on the measure in the semantic model."))
            .ToArray();
    }
}

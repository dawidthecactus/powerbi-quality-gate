using PowerBIQualityGate.Core.Inspection;
using PowerBIQualityGate.Core.Quality;
using PowerBIQualityGate.Core.Validation;

namespace PowerBIQualityGate.Core.Quality.Rules;

/// <summary>
/// Reports visible, known numeric measures without static or dynamic model-level formatting.
/// Unknown/variant types are intentionally skipped: DAX is not evaluated by this tool.
/// </summary>
public sealed class UnformattedNumericMeasureRule : IModelQualityRule
{
    public string Code => "SM-002";
    public string Title => "Format visible numeric measures";
    public DiagnosticSeverity Severity => DiagnosticSeverity.Warning;

    public IReadOnlyList<ModelQualityIssue> Evaluate(TomModelSnapshot model)
    {
        ArgumentNullException.ThrowIfNull(model);

        return model.Tables
            .SelectMany(table => table.Measures
                .Where(measure => MeasureFormattingClassifier.Classify(table, measure) ==
                    MeasureFormattingClassifier.Category.Unformatted)
                .Select(measure => new ModelQualityIssue(
                    $"{table.Name}[{measure.Name}]",
                    $"Numeric measure '{measure.Name}' has no model-level format string.",
                    "Without an explicit format, numbers can display inconsistently across reports and visuals.",
                    "Set an appropriate format string or a dynamic format-string expression on the measure in the semantic model.")))
            .ToArray();
    }
}

using PowerBIQualityGate.Core.Inspection;
using PowerBIQualityGate.Core.Validation;

namespace PowerBIQualityGate.Core.Quality.Rules;

/// <summary>
/// Flags visible columns with a conservative ID/key naming convention.
/// The name heuristic is advisory, never a claim that a column is unused.
/// </summary>
public sealed class VisibleTechnicalColumnRule : IModelQualityRule
{
    public string Code => "SM-003";
    public string Title => "Review visible technical columns";
    public DiagnosticSeverity Severity => DiagnosticSeverity.Information;

    public IReadOnlyList<ModelQualityIssue> Evaluate(TomModelSnapshot model)
    {
        ArgumentNullException.ThrowIfNull(model);

        return model.Tables
            .Where(table => !table.IsHidden)
            .SelectMany(table => table.Columns
                .Where(column => !column.IsHidden && LooksLikeTechnicalKey(column.Name))
                .Select(column => new ModelQualityIssue(
                    $"{table.Name}[{column.Name}]",
                    $"Potential technical key column '{column.Name}' is visible.",
                    "Exposing surrogate and foreign keys can clutter the Fields pane and encourage using identifiers in report visuals.",
                    "Check whether report authors need this field. If it is only a technical identifier, consider hiding it from report view.")))
            .ToArray();
    }

    private static bool LooksLikeTechnicalKey(string name)
    {
        var value = name.Trim();
        if (value.Equals("ID", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("Key", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("GUID", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (value.StartsWith("FK_", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("PK_", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return value.EndsWith(" ID", StringComparison.OrdinalIgnoreCase) ||
               value.EndsWith("_id", StringComparison.OrdinalIgnoreCase) ||
               value.EndsWith(" Key", StringComparison.OrdinalIgnoreCase) ||
               value.EndsWith("_key", StringComparison.OrdinalIgnoreCase) ||
               (value.Length > 2 && value.EndsWith("Id", StringComparison.Ordinal)) ||
               (value.Length > 2 && value.EndsWith("ID", StringComparison.Ordinal)) ||
               (value.Length > 3 && value.EndsWith("Key", StringComparison.Ordinal));
    }
}

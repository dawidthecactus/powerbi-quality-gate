using PowerBIQualityGate.Core.Inspection;
using PowerBIQualityGate.Core.Validation;

namespace PowerBIQualityGate.Core.Quality.Rules;

/// <summary>Identifies inactive relationships for informational review, not as defects.</summary>
public sealed class InactiveRelationshipRule : IModelQualityRule
{
    public string Code => "SM-004";
    public string Title => "Review inactive relationships";
    public DiagnosticSeverity Severity => DiagnosticSeverity.Information;

    public IReadOnlyList<ModelQualityIssue> Evaluate(TomModelSnapshot model)
    {
        ArgumentNullException.ThrowIfNull(model);

        return model.Relationships
            .Where(relationship => !relationship.IsActive)
            .Select(relationship => new ModelQualityIssue(
                RelationshipLocation.For(relationship),
                "Relationship is inactive and does not propagate filters by default.",
                "Inactive relationships can be intentional, for example for alternate date roles, but report authors cannot rely on their default filter propagation.",
                "Confirm the relationship is intentional. If calculations need it, inspect the measures that use USERELATIONSHIP; do not activate it without reviewing filter paths."))
            .ToArray();
    }
}

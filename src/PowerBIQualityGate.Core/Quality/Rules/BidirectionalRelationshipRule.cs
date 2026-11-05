using PowerBIQualityGate.Core.Inspection;
using PowerBIQualityGate.Core.Validation;

namespace PowerBIQualityGate.Core.Quality.Rules;

/// <summary>Flags active, explicitly bidirectional filter relationships for review.</summary>
public sealed class BidirectionalRelationshipRule : IModelQualityRule
{
    public string Code => "SM-001";
    public string Title => "Review bidirectional relationships";
    public DiagnosticSeverity Severity => DiagnosticSeverity.Warning;

    public IReadOnlyList<ModelQualityIssue> Evaluate(TomModelSnapshot model)
    {
        ArgumentNullException.ThrowIfNull(model);

        return model.Relationships
            .Where(relationship => relationship.IsActive &&
                string.Equals(relationship.CrossFilterDirection, "BothDirections", StringComparison.OrdinalIgnoreCase))
            .Select(relationship => new ModelQualityIssue(
                RelationshipLocation.For(relationship),
                "Active relationship uses bidirectional cross-filtering.",
                "Bidirectional filtering can introduce ambiguous propagation paths and affect query performance.",
                "Check whether both directions are required. Prefer single-direction filtering where the model permits it; keep justified exceptions."))
            .ToArray();
    }
}

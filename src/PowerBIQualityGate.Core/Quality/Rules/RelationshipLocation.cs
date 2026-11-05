using PowerBIQualityGate.Core.Inspection;

namespace PowerBIQualityGate.Core.Quality.Rules;

internal static class RelationshipLocation
{
    public static string For(TomRelationshipSnapshot relationship) =>
        $"{relationship.From} -> {relationship.To} (relationship: {relationship.Name})";
}

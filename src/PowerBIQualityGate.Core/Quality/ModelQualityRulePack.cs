using PowerBIQualityGate.Core.Quality.Rules;

namespace PowerBIQualityGate.Core.Quality;

/// <summary>
/// The first, conservative semantic-model quality rule catalog.
/// Construct a new engine using CreateDefault() before each audit.
/// </summary>
public static class ModelQualityRulePack
{
    public static IReadOnlyList<IModelQualityRule> CreateDefault()
    {
        IModelQualityRule[] rules =
        [
            new BidirectionalRelationshipRule(),
            new UnformattedNumericMeasureRule(),
            new VisibleTechnicalColumnRule(),
            new InactiveRelationshipRule()
        ];

        return Array.AsReadOnly(rules);
    }
}

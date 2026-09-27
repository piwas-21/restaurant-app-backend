using RestaurantSystem.Api.Common.Exceptions;

namespace RestaurantSystem.Api.Features.OptionSets.Materialization;

public sealed class OptionSetMaterializationDisabledException : ForbiddenException
{
    public const string Code = "OPTION_SET_MATERIALIZATION_DISABLED";

    public OptionSetMaterializationDisabledException()
        : base("Option-set materialization is disabled for this tenant")
    {
    }
}

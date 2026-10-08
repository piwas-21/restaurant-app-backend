using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.TenantFeatures;

namespace RestaurantSystem.Api.Features.KitchenBoard.Services;

internal static class KitchenBoardFeaturePolicy
{
    internal static bool IsEnabled(ITenantFeatures? features) =>
        features?.TableAccountV1 == true || features?.OrderAmendmentsV1 == true;

    internal static void RequireEnabled(ITenantFeatures? features)
    {
        if (!IsEnabled(features))
            throw new NotFoundException("The kitchen board workflow is not enabled.");
    }
}

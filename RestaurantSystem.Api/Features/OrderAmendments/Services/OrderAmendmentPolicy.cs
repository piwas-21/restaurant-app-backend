using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Common.TenantFeatures;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

internal static class OrderAmendmentPolicy
{
    internal static void RequireFeature(ITenantFeatures features)
    {
        if (!features.OrderAmendmentsV1)
            throw new NotFoundException("Order amendments are not enabled for this restaurant.");
    }

    internal static Guid RequireActor(ICurrentUserService user)
    {
        if (!user.IsAuthenticated || user.IsApiToken || user.UserId is not Guid actorId
            || user.Role is not (UserRole.Admin or UserRole.Cashier or UserRole.Server))
        {
            throw new ForbiddenException("Only a signed-in admin, cashier, or server may amend an order.");
        }

        return actorId;
    }

    internal static void ValidateOrderContext(
        Order source,
        OrderAmendmentQuoteRequest request)
    {
        if (source.Status is OrderStatus.Cancelled or OrderStatus.Refunded)
            throw new BadRequestException("A cancelled or refunded order cannot be amended.");

        if (source.PaymentStatus is PaymentStatus.Refunded or PaymentStatus.PartiallyRefunded
            || source.Payments.Any(payment => payment.Status is PaymentStatus.Refunded or PaymentStatus.PartiallyRefunded
                || payment.IsRefunded || payment.RefundedAmount.HasValue || payment.RefundDate.HasValue))
        {
            throw new ConflictException(
                "This order has refund activity that must be reconciled before it can be amended.");
        }

        ValidateAccountContext(source, request);

        if (request.ExpectedOrderVersion != source.Version)
            throw new ConflictException("The order changed. Refresh and quote the amendment again.");

        ValidateProviderContext(source, request);
    }

    private static void ValidateAccountContext(Order source, OrderAmendmentQuoteRequest request)
    {
        if (source.Type == OrderType.DineIn)
        {
            if (source.ServiceSession is null || source.ServiceSession.Status != TableServiceSessionStatus.Open)
                throw new ConflictException("The dine-in account is closed or unavailable.");
            if (request.ExpectedAccountRevision != source.ServiceSession.AccountRevision)
                throw new ConflictException("The table account changed. Refresh the bill and quote again.");
            if (source.ServiceSession.BillingAllocationVersion == 0
                && (source.Tip > 0 || source.DeliveryFee > 0)
                && request.Changes.Any(change => change.Kind is
                    OrderAmendmentChangeKind.Void or OrderAmendmentChangeKind.Replace))
                throw new ConflictException(
                    "This legacy account needs financial reconciliation before its food units can be removed.");
        }
        else if (source.ServiceSessionId.HasValue || request.ExpectedAccountRevision.HasValue)
        {
            throw new BadRequestException("Only a dine-in order can use a table account revision.");
        }

    }

    private static void ValidateProviderContext(Order source, OrderAmendmentQuoteRequest request)
    {
        if (source.ExternalReference is null)
        {
            if (request.LocalProviderSupplementConsent || !string.IsNullOrWhiteSpace(request.ProviderConsentNote))
                throw new BadRequestException("Provider supplement consent is only valid for a marketplace order.");
            return;
        }

        if (request.Changes.Count > 0 || !request.LocalProviderSupplementConsent
            || string.IsNullOrWhiteSpace(request.ProviderConsentNote))
        {
            throw new BadRequestException(
                "Marketplace orders allow an additions-only local supplement after explicit staff consent.");
        }

        if (request.Additions.Count == 0)
            throw new BadRequestException("A marketplace local supplement must contain new items.");
    }

    internal static void ValidateChangeAuthority(
        Order source,
        OrderAmendmentQuoteRequest request,
        ICurrentUserService user)
    {
        if (request.Changes.Count == 0)
            return;

        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new BadRequestException("A reason is required when changing an existing order line.");

        var requiresManager = source.Status is OrderStatus.OutForDelivery or OrderStatus.Delivered
            or OrderStatus.Completed;
        var requiresPreparingOverride = source.Status is OrderStatus.Preparing or OrderStatus.Ready;
        var isManager = user.IsAdmin || user.Role == UserRole.Cashier;
        var isAmendmentStaff = isManager || user.Role == UserRole.Server;
        if (requiresManager && !isManager)
            throw new ForbiddenException("Only a cashier or admin may correct a served order.");

        if (requiresPreparingOverride && (!isAmendmentStaff || !request.PreparingOverrideAcknowledged))
        {
            throw new BadRequestException(
                "A Server, cashier, or admin must acknowledge the preparing-order override and provide a reason.");
        }

        if (!source.IsKitchenReleased && request.Changes.Any(
                change => change.Kind == OrderAmendmentChangeKind.InstructionChange))
        {
            throw new BadRequestException("Instruction corrections require an order already released to the kitchen.");
        }
    }

    internal static string SanitizeText(string? value, int maxLength)
    {
        var cleaned = new string((value ?? string.Empty)
            .Select(character => char.IsControl(character) ? ' ' : character)
            .ToArray());
        var collapsed = string.Join(' ', cleaned.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return collapsed.Length <= maxLength ? collapsed : collapsed[..maxLength];
    }
}

using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Payments.Dtos;
using RestaurantSystem.Api.Features.Payments.Interfaces;

namespace RestaurantSystem.Api.Features.Payments.Queries.GetOnlinePaymentAvailabilityQuery;

/// <summary>
/// Can this restaurant take an online payment? Asked by the checkout page before it offers the
/// option (SOFRA-PAYMENTS-PLAN §5 S8).
/// </summary>
public record GetOnlinePaymentAvailabilityQuery : IQuery<ApiResponse<OnlinePaymentAvailabilityDto>>;

/// <summary>
/// Answers from configuration alone — no database, no call to Stripe.
/// </summary>
/// <remarks>
/// <para>
/// The plan words availability as "module flag AND configured account". Only the second half is
/// computed here: the first is <c>PaymentsController</c>'s class-level
/// <see cref="Common.Modules.RequireModuleAttribute"/>, which answers 404 before this handler is
/// reached. Asking <c>ITenantModules</c> again here would be unreachable code, and unreachable
/// code that looks like a safety check is worse than none — the next reader trusts it.
/// </para>
/// <para>
/// <b>Which means <see cref="IStripeGateway.IsConfigured"/> is carrying this endpoint alone on the
/// installs that matter most.</b> The module gate FAILS OPEN by design: <c>TenantModules</c> reads
/// an absent <c>TENANT_MODULES</c> list as unrestricted, which is exactly RUMI — the legacy install
/// runs the shared compose project and never receives a module list, so the 404 never fires there
/// and every module reads as bought. What keeps RUMI from being offered a payment method it cannot
/// take is that <c>Stripe:Enabled</c> is false and the keys are empty, i.e. this handler's answer.
/// </para>
/// <para>
/// It is also deliberately NOT a probe of the connected account's <c>charges_enabled</c>. That is a
/// network call to Stripe on an anonymous endpoint hit on every checkout page load, and it answers
/// a different question — a tenant mid-KYC would show as unavailable here and still be able to
/// complete a session Stripe accepts. Refusal on an account that cannot charge belongs where the
/// money is, in <c>CreateCheckoutSessionCommand</c>, which already surfaces Stripe's own refusal.
/// </para>
/// </remarks>
public class GetOnlinePaymentAvailabilityQueryHandler
    : IQueryHandler<GetOnlinePaymentAvailabilityQuery, ApiResponse<OnlinePaymentAvailabilityDto>>
{
    private readonly IStripeGateway _gateway;

    public GetOnlinePaymentAvailabilityQueryHandler(IStripeGateway gateway)
    {
        ArgumentNullException.ThrowIfNull(gateway);
        _gateway = gateway;
    }

    public Task<ApiResponse<OnlinePaymentAvailabilityDto>> Handle(
        GetOnlinePaymentAvailabilityQuery query,
        CancellationToken cancellationToken)
        => Task.FromResult(
            ApiResponse<OnlinePaymentAvailabilityDto>.SuccessWithData(
                new OnlinePaymentAvailabilityDto(_gateway.IsConfigured)));
}

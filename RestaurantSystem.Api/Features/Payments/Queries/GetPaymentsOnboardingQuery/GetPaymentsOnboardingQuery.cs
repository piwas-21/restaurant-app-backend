using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Payments.Dtos;
using RestaurantSystem.Api.Features.Payments.Interfaces;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.Api.Features.Payments.Queries.GetPaymentsOnboardingQuery;

/// <summary>
/// Where this restaurant stands on taking card payments, for its own admin
/// (SOFRA-PAYMENTS-PLAN §9 P7a).
/// </summary>
public record GetPaymentsOnboardingQuery : IQuery<ApiResponse<PaymentsOnboardingDto>>;

/// <summary>
/// Answers from configuration alone — no database, and <b>no call to Stripe</b>.
/// </summary>
/// <remarks>
/// The checklist step this backs (P5) reads "not done" until money has actually moved, which is
/// correct and is also the least informative thing a page can say. This endpoint exists so the
/// tab it links to can say something more useful than the row did: whether the restaurant is
/// even plumbed in yet, and to which account.
/// <para>
/// <b>The two refusals say different things, and both are normal operation.</b> A caller who is
/// authenticated but not an admin gets <b>403</b> from <c>AuthorizationMiddleware</c>, which runs
/// ahead of the MVC filter pipeline — so the module gate never runs and the answer cannot leak
/// what the tenant bought. A tenant that never bought the module gets the controller's class gate:
/// <b>404 carrying <c>errorCode: ModuleNotEnabled</c></b>. The code is the part that matters, since
/// a bare 404 is also what a typo'd route and a backend from before this slice answer.
/// </para>
/// <para>
/// It stops one step short of the question an owner really has — <i>has Stripe finished
/// verifying us?</i> — because answering that needs <c>GET /v1/accounts/{acct}</c> and the
/// <c>Accounts → read</c> permission the box key does not hold today. P7b adds exactly that,
/// behind a cache, and degrades to this answer when the read is refused. Saying the smaller true
/// thing is the same rule §9 Q1 binds the customer-facing copy to.
/// </para>
/// </remarks>
public class GetPaymentsOnboardingQueryHandler
    : IQueryHandler<GetPaymentsOnboardingQuery, ApiResponse<PaymentsOnboardingDto>>
{
    private readonly IStripeGateway _gateway;
    private readonly StripeSettings _settings;

    public GetPaymentsOnboardingQueryHandler(IStripeGateway gateway, IOptions<StripeSettings> settings)
    {
        ArgumentNullException.ThrowIfNull(gateway);
        ArgumentNullException.ThrowIfNull(settings);
        _gateway = gateway;
        _settings = settings.Value;
    }

    public Task<ApiResponse<PaymentsOnboardingDto>> Handle(
        GetPaymentsOnboardingQuery query, CancellationToken cancellationToken)
    {
        var configured = _gateway.IsConfigured;

        // The id is reported ONLY when the gateway is configured. `ConnectedAccountId` is the raw
        // setting and is non-empty on a half-provisioned box whose key never arrived — reporting it
        // there would show an owner an account their restaurant cannot actually transact on, which
        // is the same "one leg answering for both" mistake S8 had to fix on availability.
        var dto = new PaymentsOnboardingDto(
            configured ? PaymentsOnboardingState.Configured : PaymentsOnboardingState.NotConfigured,
            configured ? _gateway.ConnectedAccountId : null,
            _settings.DashboardUrl);

        return Task.FromResult(ApiResponse<PaymentsOnboardingDto>.SuccessWithData(dto));
    }
}

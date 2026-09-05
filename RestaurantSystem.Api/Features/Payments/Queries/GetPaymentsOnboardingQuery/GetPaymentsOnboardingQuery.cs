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
/// Answers from configuration, plus at most one CACHED read of the connected account. No database.
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
/// <b>P7b:</b> it now also asks Stripe, once and behind a cache, the question an owner really has —
/// <i>has Stripe finished verifying us?</i> — because a tenant is configured for DAYS before that
/// is true and "you are set up" is wrong for every one of them. The read needs
/// <c>Accounts → read</c> on the box key (§9 P0(b) is the decision to grant it), so it is
/// OPTIONAL by construction: when it is refused, or the account cannot be read for any other
/// reason, this returns exactly what P7a returned. Saying the smaller true thing is the same rule
/// §9 Q1 binds the customer-facing copy to.
/// </para>
/// </remarks>
public class GetPaymentsOnboardingQueryHandler
    : IQueryHandler<GetPaymentsOnboardingQuery, ApiResponse<PaymentsOnboardingDto>>
{
    private readonly IStripeGateway _gateway;
    private readonly IStripeAccountClient _accounts;
    private readonly StripeSettings _settings;
    private readonly StripeCommissionSettings _commission;

    public GetPaymentsOnboardingQueryHandler(
        IStripeGateway gateway,
        IStripeAccountClient accounts,
        IOptions<StripeSettings> settings,
        IOptions<StripeCommissionSettings> commission)
    {
        ArgumentNullException.ThrowIfNull(gateway);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(commission);
        _gateway = gateway;
        _accounts = accounts;
        _settings = settings.Value;
        _commission = commission.Value;
    }

    public async Task<ApiResponse<PaymentsOnboardingDto>> Handle(
        GetPaymentsOnboardingQuery query, CancellationToken cancellationToken)
    {
        // The id is reported ONLY when the gateway is configured. `ConnectedAccountId` is the raw
        // setting and is non-empty on a half-provisioned box whose key never arrived — reporting it
        // there would show an owner an account their restaurant cannot actually transact on, which
        // is the same "one leg answering for both" mistake S8 had to fix on availability.
        if (!_gateway.IsConfigured)
        {
            return ApiResponse<PaymentsOnboardingDto>.SuccessWithData(new PaymentsOnboardingDto(
                PaymentsOnboardingState.NotConfigured, null, PaymentsLink(),
                CommissionBps: _commission.Bps));
        }

        // Null means we could not find out — the key may not carry `Accounts → read` at all. Land
        // on P7a's answer rather than on a worse one: `configured` is the weaker claim, and it is
        // what configuration alone supports. Blanking the tab or inventing `awaitingVerification`
        // would both be reporting a Stripe verdict we never obtained.
        var account = await _accounts.GetConnectedAccountAsync(cancellationToken);

        // The count rides ONLY on the awaiting state, so it is read inside the pattern that
        // established it — on a verified account Stripe still lists future `currently_due` items
        // ahead of a deadline, and surfacing those beside "you are set up" reads as a problem where
        // there is none.
        if (account is { ChargesEnabled: false } awaitingVerification)
        {
            return ApiResponse<PaymentsOnboardingDto>.SuccessWithData(new PaymentsOnboardingDto(
                PaymentsOnboardingState.AwaitingVerification,
                _gateway.ConnectedAccountId,
                PaymentsLink(),
                awaitingVerification.RequirementsDueCount,
                _commission.Bps));
        }

        return ApiResponse<PaymentsOnboardingDto>.SuccessWithData(new PaymentsOnboardingDto(
            PaymentsOnboardingState.Configured, _gateway.ConnectedAccountId, PaymentsLink(),
            CommissionBps: _commission.Bps));
    }

    /// <summary>
    /// The link the restaurant is sent to, or null when there is none to give.
    /// </summary>
    /// <remarks>
    /// An unset setting is NOT a URL to fall back to. Under Connect Express the restaurant has no
    /// full Stripe dashboard, the links Stripe does issue live 300 seconds, and this box cannot
    /// mint one — so blank means "we have nowhere to send you", and the tab is built to say that
    /// rather than to open a login the owner does not have. Whitespace is treated as blank because
    /// an env file renders it exactly that way.
    /// </remarks>
    private string? PaymentsLink() =>
        string.IsNullOrWhiteSpace(_settings.PaymentsLinkUrl) ? null : _settings.PaymentsLinkUrl;
}

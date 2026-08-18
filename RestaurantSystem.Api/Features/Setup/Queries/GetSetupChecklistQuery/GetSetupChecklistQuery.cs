using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Modules;
using RestaurantSystem.Api.Features.Payments.Interfaces;
using RestaurantSystem.Api.Features.Setup.Dtos;
using RestaurantSystem.Api.Features.Setup.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Setup.Queries.GetSetupChecklistQuery;

/// <summary>
/// The first-run setup checklist for this tenant (SOFRA-ONBOARDING-PLAN O4).
/// Admin-only; see <c>SetupChecklistController</c>.
/// </summary>
public record GetSetupChecklistQuery : IQuery<ApiResponse<SetupChecklistDto>>;

public class GetSetupChecklistQueryHandler
    : IQueryHandler<GetSetupChecklistQuery, ApiResponse<SetupChecklistDto>>
{
    private readonly ApplicationDbContext _context;
    private readonly ITenantModules _modules;
    private readonly ISetupChecklistStore _store;
    private readonly IStripeGateway _stripe;

    public GetSetupChecklistQueryHandler(
        ApplicationDbContext context,
        ITenantModules modules,
        ISetupChecklistStore store,
        IStripeGateway stripe)
    {
        _context = context;
        _modules = modules;
        _store = store;
        _stripe = stripe;
    }

    public async Task<ApiResponse<SetupChecklistDto>> Handle(
        GetSetupChecklistQuery query, CancellationToken cancellationToken)
    {
        // No row until the first write. Absent means "nothing acknowledged, not
        // dismissed", which is exactly right for a tenant that booted a minute ago —
        // so there is nothing to seed and nothing that can be stale.
        var state = await _store.GetAsync(cancellationToken);

        var acknowledged = state is null
            ? new HashSet<string>(StringComparer.Ordinal)
            : new HashSet<string>(state.AcknowledgedSteps, StringComparer.Ordinal);

        // Entitlement first, facts second: a tenant without the payments step must not
        // pay for the query that would tick it. Nothing else here is conditional, so
        // this is the one fact that gets asked for by name.
        var entitled = SetupSteps.For(_modules, _stripe.IsConfigured).ToList();

        var facts = await ReadFactsAsync(
            needsPaymentFact: entitled.Any(s => s.Key == SetupSteps.OnlinePayments),
            cancellationToken);

        var steps = entitled
            .Select(s => new SetupStepDto(
                s.Key,
                s.ModuleId,
                s.IsDerived,
                s.IsDerived ? IsObservedDone(s.Key, facts) : acknowledged.Contains(s.Key)))
            .ToList();

        var dto = new SetupChecklistDto(
            state?.DismissedAt is not null,
            steps.Count(s => s.IsDone),
            steps);

        return ApiResponse<SetupChecklistDto>.SuccessWithData(dto);
    }

    /// <summary>What the database actually shows about this restaurant's setup.</summary>
    private readonly record struct SetupFacts(bool HasMenu, bool HasStaff, bool HasSettledCheckout);

    private async Task<SetupFacts> ReadFactsAsync(
        bool needsPaymentFact, CancellationToken cancellationToken)
    {
        // A product that is actually IN a category, not "some category exists AND some
        // product exists". Those are two independent facts, and an owner who made one
        // empty category and one uncategorised product satisfies both while having a
        // menu no guest can reach — the exact "congratulated for work nobody did"
        // failure the derived/acknowledged split exists to prevent.
        //
        // The two `!IsDeleted` predicates are written out rather than left to the global
        // filter: `ProductCategory` is a plain `Entity`, not a `SoftDeleteEntity`, so
        // the join row outlives a soft-deleted product or category and the query is
        // rooted on the join.
        var hasMenu = await _context.ProductCategories
            .AnyAsync(pc => !pc.Product.IsDeleted && !pc.Category.IsDeleted, cancellationToken);

        // STAFF, not users. Every customer who registers is an ApplicationUser too, so
        // "more than one user" would flip this step done the moment the first guest
        // signs up — an owner congratulated for staff they never invited. The seeded
        // admin is the one staff account provisioning creates (UserSeeder), so the
        // signal is a SECOND staff member.
        //
        // `!IsDeleted` is written out because ApplicationUser implements
        // IExcludeFromGlobalFilter (ADR-002): it is the ONE soft-deletable entity the
        // global filter skips, so unlike the two queries above this one is not filtered
        // for free, and a staff member who has since been removed would otherwise keep
        // the step ticked forever.
        var staffCount = await _context.Users
            .Where(u => u.Role != UserRole.Customer && !u.IsDeleted)
            .Take(2)
            .CountAsync(cancellationToken);

        // MONEY HAVING MOVED, and nothing weaker. `IsConfigured` is true the moment the
        // env vars land — days before Stripe finishes verifying the business — so a step
        // derived from it would tick for a tenant who still cannot take a card. A
        // `Created` session is only a redirect nobody may have completed. `Completed` is
        // written by the settle path after re-fetching from Stripe, which is the one
        // event that cannot happen early.
        //
        // Short-circuited when the step is not on this tenant's checklist: `false` then
        // feeds nothing, because IsObservedDone is never asked about a step that was
        // filtered out.
        var hasSettledCheckout = needsPaymentFact && await _context.OrderCheckoutSessions
            .AnyAsync(s => s.Status == CheckoutSessionStatus.Completed, cancellationToken);

        return new SetupFacts(hasMenu, staffCount > 1, hasSettledCheckout);
    }

    private static bool IsObservedDone(string key, SetupFacts facts) => key switch
    {
        SetupSteps.Menu => facts.HasMenu,
        SetupSteps.Staff => facts.HasStaff,
        SetupSteps.OnlinePayments => facts.HasSettledCheckout,
        // A derived step with no observation defined is a bug in the catalog, and the
        // safe answer to "did they do it?" is no. Marking it done would hide the step
        // and with it the bug.
        _ => false,
    };
}

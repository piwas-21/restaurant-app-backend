using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Modules;
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

    public GetSetupChecklistQueryHandler(
        ApplicationDbContext context, ITenantModules modules, ISetupChecklistStore store)
    {
        _context = context;
        _modules = modules;
        _store = store;
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

        var facts = await ReadFactsAsync(cancellationToken);

        var steps = SetupSteps.For(_modules)
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
    private readonly record struct SetupFacts(bool HasMenu, bool HasStaff);

    private async Task<SetupFacts> ReadFactsAsync(CancellationToken cancellationToken)
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

        return new SetupFacts(hasMenu, staffCount > 1);
    }

    private static bool IsObservedDone(string key, SetupFacts facts) => key switch
    {
        SetupSteps.Menu => facts.HasMenu,
        SetupSteps.Staff => facts.HasStaff,
        // A derived step with no observation defined is a bug in the catalog, and the
        // safe answer to "did they do it?" is no. Marking it done would hide the step
        // and with it the bug.
        _ => false,
    };
}

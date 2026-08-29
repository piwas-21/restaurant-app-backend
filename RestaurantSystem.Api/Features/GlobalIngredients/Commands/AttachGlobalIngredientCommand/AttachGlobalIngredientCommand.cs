using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.GlobalIngredients.Dtos;
using RestaurantSystem.Api.Features.GlobalIngredients.Services;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.GlobalIngredients.Commands.AttachGlobalIngredientCommand;

public record AttachGlobalIngredientCommand(Guid Id, AttachGlobalIngredientDto Body)
    : ICommand<ApiResponse<AttachGlobalIngredientResultDto>>;

/// <summary>
/// Copies one library row onto many products at once — plan slice <b>S8</b>, "reuse at scale", the
/// answer to <i>"why must I retype this on 40 pizzas"</i>.
/// </summary>
/// <remarks>
/// <para>
/// <b>It is a COPY, exactly as the picker's is (plan D3).</b> Name and the nine translations are
/// taken from the library row at attach time — the GROUP is stated by the caller and falls back to
/// the row's own kind, see <c>GlobalIngredientAttach.CopyOnto</c> — and <c>GlobalIngredientId</c> is
/// persisted as PROVENANCE. Nothing reads the library row afterwards and editing it still does not
/// propagate — propagation is S9, licensed by S1's order snapshot rather than by this endpoint.
/// </para>
/// <para>
/// <b>All-or-nothing on a rule violation, itemised skips otherwise.</b> A product that already
/// carries this row, or an id that resolves to no product, is REPORTED and stepped over: a bulk
/// action that refused forty products because one was already done would be unusable, and neither
/// case is an error. A product the write would leave INVALID is different — nothing is written at
/// all and the response names every offender, because that state is a money defect and the admin
/// has to decide which way out of it. Plan §6 buys protection from "irreversible bulk edit";
/// refusing before writing is how that promise is kept.
/// </para>
/// <para>
/// <b>Why there is no <c>categoryId</c> target.</b> "Apply to every pizza" is resolved by the client
/// into the ids it then sends, so the blast-radius confirm (plan D6) and the payload are the same
/// list by construction. A server-side category target would be re-resolved at save time, and a
/// product added to that category between the confirm and the save would be changed by an action
/// nobody saw.
/// </para>
/// </remarks>
public class AttachGlobalIngredientCommandHandler
    : ICommandHandler<AttachGlobalIngredientCommand, ApiResponse<AttachGlobalIngredientResultDto>>
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public AttachGlobalIngredientCommandHandler(
        ApplicationDbContext context,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<ApiResponse<AttachGlobalIngredientResultDto>> Handle(
        AttachGlobalIngredientCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Live AND not archived — the same predicate `GlobalIngredientProvenance` applies to a NEW
        // link on the product PUT. A bulk endpoint that accepted an archived row would be a second
        // door back onto a shelf the admin deliberately cleared (plan D4).
        var library = await _context.GlobalIngredients
            .Include(g => g.Translations)
            .FirstOrDefaultAsync(
                g => g.Id == command.Id && g.IsActive && g.ArchivedAt == null,
                cancellationToken);

        if (library == null)
        {
            return ApiResponse<AttachGlobalIngredientResultDto>.Failure(
                "Global ingredient not found, inactive or archived");
        }

        var requested = command.Body.ProductIds.Distinct().ToList();
        var products = await _context.Products
            .Include(p => p.DetailedIngredients)
            .Include(p => p.Variations)
            // Two COLLECTION includes in one query multiply the rows by each other, and this query is
            // the one that runs over a whole category: 40 products x 12 ingredients x 4 variations is
            // 1,920 rows carrying the product columns 48 times each. Both collections are needed —
            // the ingredients for the deduction, the variations for the price floor — so the fix is
            // to split, not to drop one.
            .AsSplitQuery()
            .Where(p => requested.Contains(p.Id))
            .ToListAsync(cancellationToken);

        // The EFFECTIVE kind, not the library row's: `CopyOnto` writes `body.Kind ?? library.Kind`
        // and the receipt has to report where the rows really went. One expression, read twice, is
        // how the picker and this endpoint stopped disagreeing about the group in the first place.
        var result = new AttachGlobalIngredientResultDto { Kind = command.Body.Kind ?? library.Kind };
        var targets = BulkCatalogAttach.Triage(
            requested,
            products,
            product => product.DetailedIngredients.Any(i => i.GlobalIngredientId == library.Id),
            result.Skipped);

        var refused = BulkCatalogAttach.Refused(targets, product => GlobalIngredientAttach.Fits(product, command.Body));
        if (refused.Count > 0)
        {
            return ApiResponse<AttachGlobalIngredientResultDto>.Failure(
                GlobalIngredientAttach.BuildRefusalMessage(refused));
        }

        var auditIdentifier = _currentUserService.GetAuditIdentifier();
        foreach (var product in targets)
        {
            GlobalIngredientAttach.CopyOnto(_context, product, library, command.Body, auditIdentifier);
            result.AttachedProductIds.Add(product.Id);
        }

        if (targets.Count > 0)
        {
            await _context.SaveChangesAsync(cancellationToken);
        }

        return ApiResponse<AttachGlobalIngredientResultDto>.SuccessWithData(result);
    }
}

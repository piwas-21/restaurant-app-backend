using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.GlobalVariations.Dtos;
using RestaurantSystem.Api.Features.GlobalVariations.Services;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.GlobalVariations.Commands.AttachGlobalVariationCommand;

public record AttachGlobalVariationCommand(Guid Id, AttachGlobalVariationDto Body)
    : ICommand<ApiResponse<AttachGlobalVariationResultDto>>;

/// <summary>
/// Copies one variation library row onto many products at once — plan slice <b>S8</b>, "reuse at
/// scale", the variation half of <i>"why must I retype this on 40 pizzas"</i>.
/// </summary>
/// <remarks>
/// <para>
/// <b>It is a COPY, exactly as the picker's is (plan D3).</b> The name and the translations are
/// taken from the library row at attach time and <c>GlobalVariationId</c> is persisted as
/// PROVENANCE. Nothing reads the library row afterwards and editing it still does not propagate —
/// propagation is S9, licensed by S1's order snapshot rather than by this endpoint.
/// </para>
/// <para>
/// <b>Why there is no "required" rule here, unlike the ingredient twin.</b> Its validator refuses a
/// REQUIRED ingredient because a pre-S1 order line renders against the LIVE recipe and would print
/// "NO &lt;name&gt;" for a removal nobody made. A variation has no such reader: an order line froze
/// its own <c>VariationName</c> at checkout, long before this plan, so adding a variation to a
/// product cannot reword a past receipt. What it CAN do is move the price floor, which is why
/// <see cref="GlobalVariationAttach.Fits"/> exists.
/// </para>
/// <para>
/// <b>Skipping is by PROVENANCE, not by name.</b> A product may legitimately carry a "Large" it
/// typed by hand; this endpoint adds the library-linked one and reports nothing, because a name
/// match is not evidence that the same row is already there — and the duplicate-name question
/// belongs to the confirm screen, which can show the admin what a name check alone would guess at.
/// </para>
/// </remarks>
public class AttachGlobalVariationCommandHandler
    : ICommandHandler<AttachGlobalVariationCommand, ApiResponse<AttachGlobalVariationResultDto>>
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public AttachGlobalVariationCommandHandler(
        ApplicationDbContext context,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<ApiResponse<AttachGlobalVariationResultDto>> Handle(
        AttachGlobalVariationCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Live AND not archived — the same predicate `GlobalVariationProvenance` applies to a NEW
        // link on the product PUT. A bulk endpoint that accepted an archived row would be a second
        // door back onto a shelf the admin deliberately cleared (plan D4).
        var library = await _context.GlobalVariations
            .Include(g => g.Translations)
            .FirstOrDefaultAsync(
                g => g.Id == command.Id && g.IsActive && g.ArchivedAt == null,
                cancellationToken);

        if (library == null)
        {
            return ApiResponse<AttachGlobalVariationResultDto>.Failure(
                "Global variation not found, inactive or archived");
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

        var result = new AttachGlobalVariationResultDto();
        var targets = BulkCatalogAttach.Triage(
            requested,
            products,
            product => product.Variations.Any(v => v.GlobalVariationId == library.Id),
            result.Skipped);

        var refused = BulkCatalogAttach.Refused(targets, product => GlobalVariationAttach.Fits(product, command.Body));
        if (refused.Count > 0)
        {
            return ApiResponse<AttachGlobalVariationResultDto>.Failure(
                GlobalVariationAttach.BuildRefusalMessage(refused));
        }

        var auditIdentifier = _currentUserService.GetAuditIdentifier();
        foreach (var product in targets)
        {
            GlobalVariationAttach.CopyOnto(_context, product, library, command.Body, auditIdentifier);
            result.AttachedProductIds.Add(product.Id);
        }

        if (targets.Count > 0)
        {
            await _context.SaveChangesAsync(cancellationToken);
        }

        return ApiResponse<AttachGlobalVariationResultDto>.SuccessWithData(result);
    }
}

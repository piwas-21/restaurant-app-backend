using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.GlobalIngredients.Services;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.GlobalIngredients.Commands.DeleteGlobalIngredientCommand;

public record DeleteGlobalIngredientCommand(Guid Id) : ICommand<ApiResponse<string>>;

/// <summary>
/// One admin action, two outcomes, decided by the reverse link (plan D4: <i>"a catalog row in use is
/// archived, never removed"</i>).
///
/// <para>
/// <b>Nothing uses it</b> — soft delete, exactly as §9.18 left it: the row is flagged, its
/// translations survive, and the global query filter hides it from every read.
/// </para>
///
/// <para>
/// <b>Some product uses it</b> — ARCHIVE instead. The difference is not cosmetic. A soft delete is
/// invisible to the query filter, which un-includes it from the product detail projection too, so
/// deleting a library row a product actually used silently emptied that ingredient's translations —
/// the behaviour <c>ProductDetail_DoesNotServeADeletedGlobalsTranslations</c> pins for rows already
/// in that state. An archived row is merely off the shelf: no picker offers it, no product may
/// newly link to it, every product that already links to it keeps its provenance AND its text, and
/// <c>restore</c> puts it back.
/// </para>
///
/// <para>
/// The count is the same one the picker renders, so the UI can label the button honestly before the
/// admin presses it — "Archive" above zero, "Delete" at zero.
/// </para>
/// </summary>
public class DeleteGlobalIngredientCommandHandler : ICommandHandler<DeleteGlobalIngredientCommand, ApiResponse<string>>
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public DeleteGlobalIngredientCommandHandler(
        ApplicationDbContext context,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<ApiResponse<string>> Handle(DeleteGlobalIngredientCommand command, CancellationToken cancellationToken)
    {
        var ingredient = await _context.GlobalIngredients
            .FirstOrDefaultAsync(g => g.Id == command.Id, cancellationToken);

        if (ingredient == null)
        {
            return ApiResponse<string>.Failure("Global ingredient not found");
        }

        if (ingredient.ArchivedAt.HasValue)
        {
            // Archiving twice would re-stamp who archived it and when, losing the only record of
            // the first one. The row is still visible here — unlike a soft delete, which the query
            // filter turns into the "not found" above on a second attempt.
            return ApiResponse<string>.Failure("Global ingredient is already archived");
        }

        var usedOnProductCount = await GlobalIngredientUsage.CountForAsync(_context, ingredient.Id, cancellationToken);

        if (usedOnProductCount > 0)
        {
            ingredient.ArchivedAt = DateTime.UtcNow;
            ingredient.ArchivedBy = _currentUserService.GetAuditIdentifier();

            await _context.SaveChangesAsync(cancellationToken);

            return ApiResponse<string>.SuccessWithData(
                $"Global ingredient archived; {usedOnProductCount} product(s) still reference it");
        }

        // Set by hand, as every other soft delete in the codebase does. This used to be a `Remove()`
        // under a comment claiming the entity configuration handled the soft delete; nothing did.
        // `ApplicationDbContext.ApplyAuditInformation()` converts `EntityState.Deleted` into
        // `IsDeleted`, but it used to run only from the SYNCHRONOUS `SaveChanges()` override, and
        // every handler awaits `SaveChangesAsync` — so it never ran, and the `Remove()` reached the
        // database as a permanent DELETE (and, where a product still referenced the row, as a
        // foreign-key error).
        //
        // §9.18's root fix has since landed: `SaveChangesAsync` is overridden too, so a `Remove()`
        // here WOULD now soft-delete correctly. The explicit form is kept anyway — it is what the
        // rest of the codebase does, it states the intent at the callsite, and it keeps the audit
        // identity the handler's decision rather than the ambient one.
        ingredient.IsDeleted = true;
        ingredient.DeletedAt = DateTime.UtcNow;
        ingredient.DeletedBy = _currentUserService.GetAuditIdentifier();

        await _context.SaveChangesAsync(cancellationToken);

        return ApiResponse<string>.SuccessWithData("Global ingredient deleted successfully");
    }
}

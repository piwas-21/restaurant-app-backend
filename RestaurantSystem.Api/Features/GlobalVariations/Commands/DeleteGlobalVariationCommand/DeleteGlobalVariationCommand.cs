using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.GlobalVariations.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.GlobalVariations.Commands.DeleteGlobalVariationCommand;

public record DeleteGlobalVariationCommand(Guid Id) : ICommand<ApiResponse<string>>;

/// <summary>
/// One admin action, two outcomes, decided by the reverse link — the rule S3 established for the
/// ingredient library (plan D4: <i>"a catalog row in use is archived, never removed"</i>) and this
/// slice inherits rather than re-argues.
///
/// <para>
/// <b>Some product uses it</b> — ARCHIVE. The row stays readable, keeps serving the products that
/// copied it, disappears from the picker, and <c>restore</c> reverses it.
/// <b>Nothing uses it</b> — soft delete, which the global query filter then hides from every read.
/// </para>
///
/// <para>
/// <b>A platform-seeded row is ARCHIVED whatever its usage count, never removed</b> (plan D14).
/// These catalogs are per-tenant TABLES seeded with platform rows, so an unused built-in was
/// indistinguishable from a name the admin typed, and the picker offered "Delete" on all 704 of
/// them. Archiving stays available for both — "we do not sell that" is a thing a tenant has to be
/// able to say about a shipped row — and the picker hides the destructive control for built-ins,
/// but the rule is enforced HERE so a client that ignores <c>origin</c> cannot get round it.
/// </para>
/// </summary>
public class DeleteGlobalVariationCommandHandler : ICommandHandler<DeleteGlobalVariationCommand, ApiResponse<string>>
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public DeleteGlobalVariationCommandHandler(
        ApplicationDbContext context,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<ApiResponse<string>> Handle(DeleteGlobalVariationCommand command, CancellationToken cancellationToken)
    {
        var variation = await _context.GlobalVariations
            .FirstOrDefaultAsync(g => g.Id == command.Id, cancellationToken);

        if (variation == null)
        {
            return ApiResponse<string>.Failure("Global variation not found");
        }

        if (variation.ArchivedAt.HasValue)
        {
            // Archiving twice would re-stamp who archived it and when, losing the only record of the
            // first one. The row is still visible here, unlike a soft delete, which the query filter
            // turns into the "not found" above on a second attempt.
            return ApiResponse<string>.Failure("Global variation is already archived");
        }

        var usedOnProductCount = await GlobalVariationUsage.CountForAsync(_context, variation.Id, cancellationToken);

        // A built-in is archived at any usage count — including zero, which is the case the picker
        // used to label "Delete".
        if (usedOnProductCount > 0 || variation.Origin == LibraryOrigin.System)
        {
            variation.ArchivedAt = DateTime.UtcNow;
            variation.ArchivedBy = _currentUserService.GetAuditIdentifier();

            await _context.SaveChangesAsync(cancellationToken);

            return ApiResponse<string>.SuccessWithData(variation.Origin == LibraryOrigin.System
                ? "Built-in variation archived; built-in library rows are never removed"
                : $"Global variation archived; {usedOnProductCount} product(s) still reference it");
        }

        // Set by hand, as every other soft delete in this codebase does: it states the intent at the
        // callsite and keeps the audit identity the handler's decision rather than the ambient one.
        variation.IsDeleted = true;
        variation.DeletedAt = DateTime.UtcNow;
        variation.DeletedBy = _currentUserService.GetAuditIdentifier();

        await _context.SaveChangesAsync(cancellationToken);

        return ApiResponse<string>.SuccessWithData("Global variation deleted successfully");
    }
}

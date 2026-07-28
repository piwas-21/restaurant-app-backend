using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.GlobalIngredients.Commands.DeleteGlobalIngredientCommand;

public record DeleteGlobalIngredientCommand(Guid Id) : ICommand<ApiResponse<string>>;

public class DeleteGlobalIngredientCommandHandler : ICommandHandler<DeleteGlobalIngredientCommand, ApiResponse<string>>
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public DeleteGlobalIngredientCommandHandler(ApplicationDbContext context, ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<ApiResponse<string>> Handle(DeleteGlobalIngredientCommand command, CancellationToken cancellationToken)
    {
        var ingredient = await _context.GlobalIngredients.FindAsync(new object[] { command.Id }, cancellationToken);

        if (ingredient == null)
        {
            return ApiResponse<string>.Failure("Global ingredient not found");
        }

        // Set the flag; do NOT call Remove(). The comment this replaces said "soft delete handled by
        // entity type configuration", and it was false in a way nothing could see: the conversion
        // from Deleted to IsDeleted lives in `ApplicationDbContext.ApplyAuditInformation`, which is
        // called ONLY from the synchronous `SaveChanges()` override. Every handler — including this
        // one — calls `SaveChangesAsync`, so `Remove()` here permanently deleted the row.
        //
        // Measured, not inferred: `Remove()` + `SaveChangesAsync` on a soft-delete entity leaves no
        // row even under `IgnoreQueryFilters()`, and takes its dependent rows with it. Every other
        // delete command in this codebase already sets the flag by hand — that assignment IS the
        // workaround for the same hole, which is why this was the only command affected.
        //
        // The root cause (overriding `SaveChangesAsync` too) is a live-system behaviour change:
        // audit columns would start being auto-stamped over handler-set values, and every `Remove()`
        // of a soft-delete entity would silently become a soft delete. It needs its own PR and a
        // full run; this closes the one caller that actually relied on the broken promise.
        ingredient.IsDeleted = true;
        ingredient.DeletedAt = DateTime.UtcNow;
        ingredient.DeletedBy = _currentUserService.GetAuditIdentifier();
        await _context.SaveChangesAsync(cancellationToken);

        return ApiResponse<string>.SuccessWithData("Global ingredient deleted successfully");
    }
}

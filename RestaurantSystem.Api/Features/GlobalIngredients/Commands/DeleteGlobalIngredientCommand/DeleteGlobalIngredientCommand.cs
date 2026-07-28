using Microsoft.EntityFrameworkCore;
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

        // Set by hand, as every other soft delete in the codebase does. This used to be a `Remove()`
        // under a comment claiming the entity configuration handled the soft delete; nothing does.
        // `ApplicationDbContext` DOES convert `EntityState.Deleted` into `IsDeleted`, in
        // `ApplyAuditInformation()` — but that is called only from the SYNCHRONOUS `SaveChanges()`
        // override, and every handler awaits `SaveChangesAsync`, so it never ran. The `Remove()`
        // reached the database as a permanent DELETE (and, where a product still referenced the row,
        // as a foreign-key error). Do not reintroduce it before §9.18's root fix lands.
        ingredient.IsDeleted = true;
        ingredient.DeletedAt = DateTime.UtcNow;
        ingredient.DeletedBy = _currentUserService.GetAuditIdentifier();

        await _context.SaveChangesAsync(cancellationToken);

        return ApiResponse<string>.SuccessWithData("Global ingredient deleted successfully");
    }
}

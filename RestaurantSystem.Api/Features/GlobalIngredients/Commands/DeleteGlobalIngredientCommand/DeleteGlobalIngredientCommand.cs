using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.GlobalIngredients.Commands.DeleteGlobalIngredientCommand;

public record DeleteGlobalIngredientCommand(Guid Id) : ICommand<ApiResponse<string>>;

public class DeleteGlobalIngredientCommandHandler : ICommandHandler<DeleteGlobalIngredientCommand, ApiResponse<string>>
{
    private readonly ApplicationDbContext _context;

    public DeleteGlobalIngredientCommandHandler(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ApiResponse<string>> Handle(DeleteGlobalIngredientCommand command, CancellationToken cancellationToken)
    {
        var ingredient = await _context.GlobalIngredients.FindAsync(new object[] { command.Id }, cancellationToken);

        if (ingredient == null)
        {
            return ApiResponse<string>.Failure("Global ingredient not found");
        }

        // Soft delete handled by entity type configuration.
        _context.GlobalIngredients.Remove(ingredient);
        await _context.SaveChangesAsync(cancellationToken);

        return ApiResponse<string>.SuccessWithData("Global ingredient deleted successfully");
    }
}

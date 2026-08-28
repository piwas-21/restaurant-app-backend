using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.GlobalVariations.Dtos;
using RestaurantSystem.Api.Features.GlobalVariations.Services;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.GlobalVariations.Commands.RestoreGlobalVariationCommand;

public record RestoreGlobalVariationCommand(Guid Id) : ICommand<ApiResponse<GlobalVariationDto>>;

/// <summary>
/// Puts an archived library row back on the shelf. It restores an ARCHIVED row and nothing else: a
/// soft-deleted row is invisible to this lookup, so it reports "not found" — undoing that would mean
/// reading through the global query filter, and the catalog only ever soft-deletes a row no product
/// uses. The asymmetry is D4's, documented there.
/// </summary>
public class RestoreGlobalVariationCommandHandler : ICommandHandler<RestoreGlobalVariationCommand, ApiResponse<GlobalVariationDto>>
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public RestoreGlobalVariationCommandHandler(
        ApplicationDbContext context,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<ApiResponse<GlobalVariationDto>> Handle(RestoreGlobalVariationCommand command, CancellationToken cancellationToken)
    {
        var variation = await _context.GlobalVariations
            .Include(g => g.Translations)
            .FirstOrDefaultAsync(g => g.Id == command.Id, cancellationToken);

        if (variation == null)
        {
            return ApiResponse<GlobalVariationDto>.Failure("Global variation not found");
        }

        if (!variation.ArchivedAt.HasValue)
        {
            return ApiResponse<GlobalVariationDto>.Failure("Global variation is not archived");
        }

        variation.ArchivedAt = null;
        variation.ArchivedBy = null;
        variation.UpdatedAt = DateTime.UtcNow;
        variation.UpdatedBy = _currentUserService.GetAuditIdentifier();

        await _context.SaveChangesAsync(cancellationToken);

        var usedOnProductCount = await GlobalVariationUsage.CountForAsync(_context, variation.Id, cancellationToken);

        return ApiResponse<GlobalVariationDto>.SuccessWithData(
            GlobalVariationMapper.ToDto(variation, usedOnProductCount));
    }
}

using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.RestaurantInfo.Dtos;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.RestaurantInfo.Commands.UpdateRestaurantInfoCommand;

/// <summary>
/// Updates the singleton <see cref="Domain.Entities.RestaurantInfo"/> row.
/// Phone numbers are managed through dedicated phone CRUD commands;
/// this command updates only the singleton's own fields.
/// Full-replace semantics: every field is assigned unconditionally, so an
/// omitted/null <see cref="ThemePaletteKey"/> or entrance position CLEARS the
/// stored value (entrance falls back to the frontend's default position).
/// </summary>
public record UpdateRestaurantInfoCommand(
    string Name,
    string AddressLine1,
    string? AddressLine2,
    string City,
    string PostalCode,
    string Country,
    decimal? Latitude,
    decimal? Longitude,
    string Email,
    string? Website,
    string? ThemePaletteKey = null,
    decimal? EntrancePositionX = null,
    decimal? EntrancePositionY = null
) : ICommand<ApiResponse<RestaurantInfoDto>>;

public class UpdateRestaurantInfoCommandHandler
    : ICommandHandler<UpdateRestaurantInfoCommand, ApiResponse<RestaurantInfoDto>>
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public UpdateRestaurantInfoCommandHandler(
        ApplicationDbContext context,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<ApiResponse<RestaurantInfoDto>> Handle(
        UpdateRestaurantInfoCommand command, CancellationToken cancellationToken)
    {
        // Required-field and format validation runs in UpdateRestaurantInfoCommandValidator,
        // executed by ValidationBehavior in the CustomMediator pipeline before this handler
        // (returns 400 on failure). Keep this handler focused on persistence.
        var info = await _context.RestaurantInfo
            .Include(r => r.PhoneNumbers)
            .FirstOrDefaultAsync(cancellationToken);

        if (info is null)
        {
            throw new NotFoundException("Restaurant info has not been initialised.");
        }

        info.Name = command.Name;
        info.AddressLine1 = command.AddressLine1;
        info.AddressLine2 = command.AddressLine2;
        info.City = command.City;
        info.PostalCode = command.PostalCode;
        info.Country = command.Country;
        info.Latitude = command.Latitude;
        info.Longitude = command.Longitude;
        info.Email = command.Email;
        info.Website = command.Website;
        info.ThemePaletteKey = command.ThemePaletteKey;
        info.EntrancePositionX = command.EntrancePositionX;
        info.EntrancePositionY = command.EntrancePositionY;
        info.UpdatedAt = DateTime.UtcNow;
        info.UpdatedBy = _currentUserService.GetAuditIdentifier();

        await _context.SaveChangesAsync(cancellationToken);

        return ApiResponse<RestaurantInfoDto>.SuccessWithData(new RestaurantInfoDto(
            info.Id, info.Name, info.AddressLine1, info.AddressLine2,
            info.City, info.PostalCode, info.Country, info.Latitude, info.Longitude,
            info.Email, info.Website, info.ThemePaletteKey,
            info.EntrancePositionX, info.EntrancePositionY,
            info.PhoneNumbers
                .OrderBy(p => p.DisplayOrder)
                .Select(p => new RestaurantPhoneNumberDto(
                    p.Id, p.Label, p.Number, p.WhatsAppEnabled, p.DisplayOrder, p.IsActive))
                .ToList()));
    }
}

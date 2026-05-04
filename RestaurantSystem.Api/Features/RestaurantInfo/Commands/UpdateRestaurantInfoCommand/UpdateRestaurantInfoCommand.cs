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
    string? Website
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
        // TODO(#9): replace these inline guards with a FluentValidation
        // validator once the validation pipeline is wired into CustomMediator.
        if (string.IsNullOrWhiteSpace(command.Name)) throw new BadRequestException("Name is required.");
        if (string.IsNullOrWhiteSpace(command.AddressLine1)) throw new BadRequestException("AddressLine1 is required.");
        if (string.IsNullOrWhiteSpace(command.City)) throw new BadRequestException("City is required.");
        if (string.IsNullOrWhiteSpace(command.PostalCode)) throw new BadRequestException("PostalCode is required.");
        if (string.IsNullOrWhiteSpace(command.Country)) throw new BadRequestException("Country is required.");
        if (string.IsNullOrWhiteSpace(command.Email)) throw new BadRequestException("Email is required.");

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
        info.UpdatedAt = DateTime.UtcNow;
        info.UpdatedBy = _currentUserService.GetAuditIdentifier();

        await _context.SaveChangesAsync(cancellationToken);

        return ApiResponse<RestaurantInfoDto>.SuccessWithData(new RestaurantInfoDto(
            info.Id, info.Name, info.AddressLine1, info.AddressLine2,
            info.City, info.PostalCode, info.Country, info.Latitude, info.Longitude,
            info.Email, info.Website,
            info.PhoneNumbers
                .OrderBy(p => p.DisplayOrder)
                .Select(p => new RestaurantPhoneNumberDto(
                    p.Id, p.Label, p.Number, p.WhatsAppEnabled, p.DisplayOrder, p.IsActive))
                .ToList()));
    }
}

using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.RestaurantInfo.Dtos;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.RestaurantInfo.Commands.UpdatePhoneNumberCommand;

public record UpdatePhoneNumberCommand(
    Guid Id,
    string? Label,
    string Number,
    bool WhatsAppEnabled,
    int DisplayOrder,
    bool IsActive
) : ICommand<ApiResponse<RestaurantPhoneNumberDto>>;

public class UpdatePhoneNumberCommandHandler
    : ICommandHandler<UpdatePhoneNumberCommand, ApiResponse<RestaurantPhoneNumberDto>>
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public UpdatePhoneNumberCommandHandler(ApplicationDbContext context, ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<ApiResponse<RestaurantPhoneNumberDto>> Handle(
        UpdatePhoneNumberCommand command, CancellationToken cancellationToken)
    {
        if (!PhoneNumberValidation.IsValid(command.Number))
        {
            throw new BadRequestException(
                "Phone number must be in E.164 format (e.g. +41227863333).");
        }

        var phone = await _context.RestaurantPhoneNumbers
            .FirstOrDefaultAsync(p => p.Id == command.Id, cancellationToken);

        if (phone is null)
        {
            throw new NotFoundException($"Phone number {command.Id} not found.");
        }

        phone.Label = command.Label;
        phone.Number = command.Number;
        phone.WhatsAppEnabled = command.WhatsAppEnabled;
        phone.DisplayOrder = command.DisplayOrder;
        phone.IsActive = command.IsActive;
        phone.UpdatedAt = DateTime.UtcNow;
        phone.UpdatedBy = _currentUserService.GetAuditIdentifier();

        await _context.SaveChangesAsync(cancellationToken);

        return ApiResponse<RestaurantPhoneNumberDto>.SuccessWithData(new RestaurantPhoneNumberDto(
            phone.Id, phone.Label, phone.Number, phone.WhatsAppEnabled, phone.DisplayOrder, phone.IsActive));
    }
}

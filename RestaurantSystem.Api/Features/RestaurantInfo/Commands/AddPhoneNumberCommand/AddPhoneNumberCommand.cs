using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.RestaurantInfo.Dtos;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.RestaurantInfo.Commands.AddPhoneNumberCommand;

public record AddPhoneNumberCommand(
    string? Label,
    string Number,
    bool WhatsAppEnabled,
    int DisplayOrder,
    bool IsActive
) : ICommand<ApiResponse<RestaurantPhoneNumberDto>>;

public class AddPhoneNumberCommandHandler
    : ICommandHandler<AddPhoneNumberCommand, ApiResponse<RestaurantPhoneNumberDto>>
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public AddPhoneNumberCommandHandler(ApplicationDbContext context, ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<ApiResponse<RestaurantPhoneNumberDto>> Handle(
        AddPhoneNumberCommand command, CancellationToken cancellationToken)
    {
        if (!PhoneNumberValidation.IsValid(command.Number))
        {
            throw new BadRequestException(
                "Phone number must be in E.164 format (e.g. +41227863333).");
        }

        var info = await _context.RestaurantInfo.FirstOrDefaultAsync(cancellationToken);
        if (info is null)
        {
            throw new NotFoundException("Restaurant info has not been initialised.");
        }

        var phone = new RestaurantPhoneNumber
        {
            RestaurantInfoId = info.Id,
            Label = command.Label,
            Number = command.Number,
            WhatsAppEnabled = command.WhatsAppEnabled,
            DisplayOrder = command.DisplayOrder,
            IsActive = command.IsActive,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = _currentUserService.GetAuditIdentifier(),
        };

        _context.RestaurantPhoneNumbers.Add(phone);
        await _context.SaveChangesAsync(cancellationToken);

        return ApiResponse<RestaurantPhoneNumberDto>.SuccessWithData(new RestaurantPhoneNumberDto(
            phone.Id, phone.Label, phone.Number, phone.WhatsAppEnabled, phone.DisplayOrder, phone.IsActive));
    }
}

using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.RestaurantInfo.Commands.DeletePhoneNumberCommand;

public record DeletePhoneNumberCommand(Guid Id) : ICommand<ApiResponse<Guid>>;

public class DeletePhoneNumberCommandHandler
    : ICommandHandler<DeletePhoneNumberCommand, ApiResponse<Guid>>
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<DeletePhoneNumberCommandHandler> _logger;

    public DeletePhoneNumberCommandHandler(
        ApplicationDbContext context,
        ICurrentUserService currentUserService,
        ILogger<DeletePhoneNumberCommandHandler> logger)
    {
        _context = context;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<ApiResponse<Guid>> Handle(
        DeletePhoneNumberCommand command, CancellationToken cancellationToken)
    {
        var phone = await _context.RestaurantPhoneNumbers
            .FirstOrDefaultAsync(p => p.Id == command.Id, cancellationToken);

        if (phone is null)
        {
            throw new NotFoundException($"Phone number {command.Id} not found.");
        }

        _context.RestaurantPhoneNumbers.Remove(phone);
        await _context.SaveChangesAsync(cancellationToken);

        // Hard delete leaves no audit row on the entity itself; log the
        // deletion so the actor + number is still traceable in app logs.
        _logger.LogInformation(
            "Restaurant phone number {PhoneId} ({Number}) deleted by {User}",
            phone.Id, phone.Number, _currentUserService.GetAuditIdentifier());

        return ApiResponse<Guid>.SuccessWithData(command.Id);
    }
}

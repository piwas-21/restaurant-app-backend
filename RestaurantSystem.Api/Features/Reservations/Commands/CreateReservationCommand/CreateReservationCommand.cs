using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Reservations.Dtos;
using RestaurantSystem.Api.Features.Reservations.Services;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Reservations.Commands.CreateReservationCommand;

public record CreateReservationCommand(CreateReservationDto ReservationData, Guid? CustomerId = null)
    : ICommand<ApiResponse<ReservationDto>>;

public class CreateReservationCommandHandler(ApplicationDbContext context, IReservationCreatedMailer mailer,
    IReservationDraftWriter writer, IPreferredLanguageCapture languages, ILogger<CreateReservationCommandHandler> logger)
    : ICommandHandler<CreateReservationCommand, ApiResponse<ReservationDto>>
{
    public async Task<ApiResponse<ReservationDto>> Handle(CreateReservationCommand command, CancellationToken cancellationToken)
    {
        try
        {
            var language = await languages.ForUserAsync(command.CustomerId, cancellationToken);
            await using var transaction = await ReservationWriteLock.BeginAsync(context, cancellationToken);
            var draft = await writer.CreateUnderLockAsync(command.ReservationData, command.CustomerId, language, cancellationToken);
            if (draft.Reservation is not { } reservation || draft.TableNumber is not { } tableNumber)
                return ApiResponse<ReservationDto>.Failure(draft.Error ?? "Failed to create reservation");
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            await mailer.SendAsync(reservation, tableNumber, cancellationToken);
            logger.LogInformation("Created reservation {ReservationId} for table {TableNumber} on {Date}",
                reservation.Id, tableNumber, reservation.ReservationDate);
            return ApiResponse<ReservationDto>.SuccessWithData(ReservationDtoMapper.ToDto(reservation, tableNumber),
                "Reservation created successfully. You will receive a confirmation email once approved.");
        }
        catch (Exception ex) when (ex is not Common.Exceptions.BadRequestException)
        {
            logger.LogError(ex, "Error creating reservation");
            return ApiResponse<ReservationDto>.Failure("Failed to create reservation");
        }
    }
}

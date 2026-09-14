using System.Text.Json.Serialization;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Commands.CreateOrderOperationalNoteCommand;

public record CreateOrderOperationalNoteCommand : ICommand<ApiResponse<OrderOperationalNoteDto>>
{
    // Route-owned identity: [JsonIgnore] keeps a body value from binding, so the request can
    // never even carry a different order id for the handler to overwrite (S6964 under-posting).
    [JsonIgnore]
    public Guid OrderId { get; set; }

    public string Text { get; set; } = string.Empty;

    // Required so an omitted value cannot silently default (S6964).
    [JsonRequired]
    public OrderNoteAudience Audience { get; set; }

    // Required: the idempotency key — a note without one is not retry-safe (S6964).
    [JsonRequired]
    public Guid ClientOperationId { get; set; }

    /// <summary>Optional detail version; old clients may omit it.</summary>
    public int? ExpectedVersion { get; set; }
}

public class CreateOrderOperationalNoteCommandValidator : AbstractValidator<CreateOrderOperationalNoteCommand>
{
    public CreateOrderOperationalNoteCommandValidator()
    {
        RuleFor(command => command.ExpectedVersion)
            .GreaterThan(0)
            .When(command => command.ExpectedVersion.HasValue)
            .WithMessage("Expected version must be greater than zero");

        RuleFor(command => command.OrderId).NotEmpty();
        RuleFor(command => command.Text).NotEmpty().MaximumLength(500);
        RuleFor(command => command.Audience).IsInEnum();
        RuleFor(command => command.ClientOperationId).NotEmpty();
    }
}

public class CreateOrderOperationalNoteCommandHandler
    : ICommandHandler<CreateOrderOperationalNoteCommand, ApiResponse<OrderOperationalNoteDto>>
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public CreateOrderOperationalNoteCommandHandler(
        ApplicationDbContext context,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<ApiResponse<OrderOperationalNoteDto>> Handle(
        CreateOrderOperationalNoteCommand command,
        CancellationToken cancellationToken)
    {
        var text = command.Text.Trim();
        if (text.Length == 0)
        {
            return ApiResponse<OrderOperationalNoteDto>.Failure("Note text is required");
        }

        var existing = await _context.OrderOperationalNotes
            .AsNoTracking()
            .SingleOrDefaultAsync(note => note.OrderId == command.OrderId
                && note.ClientOperationId == command.ClientOperationId, cancellationToken);
        if (existing is not null)
        {
            if (existing.Text != text || existing.Audience != command.Audience)
            {
                return ApiResponse<OrderOperationalNoteDto>.Failure("This note operation was already used with different content");
            }

            return ApiResponse<OrderOperationalNoteDto>.SuccessWithData(ToDto(existing));
        }

        var order = await _context.Orders
            .FirstOrDefaultAsync(order => order.Id == command.OrderId && !order.IsDeleted, cancellationToken);
        if (order is null)
        {
            return ApiResponse<OrderOperationalNoteDto>.Failure("Order not found");
        }

        if (command.ExpectedVersion.HasValue && order.Version != command.ExpectedVersion.Value)
        {
            return ApiResponse<OrderOperationalNoteDto>.FailureWithCode(
                "The order changed. Refresh it before adding the note.",
                ErrorCodes.OrderVersionConflict);
        }

        // Notes are part of the order detail state. Touching the aggregate makes a previously read
        // version stale, while the DbContext's audit hook increments Version atomically with the note.
        order.UpdatedAt = DateTime.UtcNow;
        order.UpdatedBy = _currentUserService.GetAuditIdentifier();

        var note = new OrderOperationalNote
        {
            OrderId = command.OrderId,
            Text = text,
            Audience = command.Audience,
            ClientOperationId = command.ClientOperationId,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = _currentUserService.GetAuditIdentifier()
        };
        _context.OrderOperationalNotes.Add(note);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiResponse<OrderOperationalNoteDto>.FailureWithCode(
                "The order changed. Refresh it before adding the note.",
                ErrorCodes.OrderVersionConflict);
        }

        return ApiResponse<OrderOperationalNoteDto>.SuccessWithData(ToDto(note), "Note saved");
    }

    internal static OrderOperationalNoteDto ToDto(OrderOperationalNote note) => new()
    {
        Id = note.Id,
        OrderId = note.OrderId,
        Text = note.Text,
        Audience = note.Audience.ToString(),
        CreatedAt = note.CreatedAt,
        CreatedBy = note.CreatedBy,
        ClientOperationId = note.ClientOperationId
    };
}

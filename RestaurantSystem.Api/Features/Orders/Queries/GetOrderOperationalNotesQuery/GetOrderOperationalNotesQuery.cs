using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Orders.Commands.CreateOrderOperationalNoteCommand;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Queries.GetOrderOperationalNotesQuery;

public record GetOrderOperationalNotesQuery(Guid OrderId) : IQuery<ApiResponse<List<OrderOperationalNoteDto>>>;

public class GetOrderOperationalNotesQueryHandler
    : IQueryHandler<GetOrderOperationalNotesQuery, ApiResponse<List<OrderOperationalNoteDto>>>
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public GetOrderOperationalNotesQueryHandler(ApplicationDbContext context, ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<ApiResponse<List<OrderOperationalNoteDto>>> Handle(
        GetOrderOperationalNotesQuery query,
        CancellationToken cancellationToken)
    {
        var orderExists = await _context.Orders
            .AnyAsync(order => order.Id == query.OrderId && !order.IsDeleted, cancellationToken);
        if (!orderExists)
        {
            return ApiResponse<List<OrderOperationalNoteDto>>.Failure("Order not found");
        }

        var notes = _context.OrderOperationalNotes
            .AsNoTracking()
            .Where(note => note.OrderId == query.OrderId);

        // Kitchen displays must not receive staff-only operational commentary.
        if (_currentUserService.Role == UserRole.KitchenStaff)
        {
            notes = notes.Where(note => note.Audience == OrderNoteAudience.Kitchen);
        }

        var persistedNotes = await notes.OrderBy(note => note.CreatedAt).ThenBy(note => note.Id)
            .ToListAsync(cancellationToken);
        var result = persistedNotes.Select(CreateOrderOperationalNoteCommandHandler.ToDto).ToList();
        return ApiResponse<List<OrderOperationalNoteDto>>.SuccessWithData(result);
    }
}

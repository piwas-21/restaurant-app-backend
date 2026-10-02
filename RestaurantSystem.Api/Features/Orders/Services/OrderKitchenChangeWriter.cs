using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <summary>Stages a durable per-destination preparation job with the amendment, before commit.</summary>
public sealed class OrderKitchenChangeWriter : IOrderKitchenChangeWriter
{
    private const int MaximumSummaryLength = 500;
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;

    public OrderKitchenChangeWriter(ApplicationDbContext context, ICurrentUserService currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<Guid> StageAsync(Order order, Guid amendmentId, long? accountRevision,
        DevicePrintTarget target, IReadOnlyList<PrinterFeedChangeDto> changes,
        string summary, CancellationToken cancellationToken)
    {
        if (_context.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Kitchen changes must be staged inside the amendment transaction.");
        }

        if (order.Id == Guid.Empty || amendmentId == Guid.Empty || !order.IsKitchenReleased
            || accountRevision is <= 0
            || target is not (DevicePrintTarget.FrontKitchen or DevicePrintTarget.BackKitchen
                or DevicePrintTarget.General or DevicePrintTarget.Default))
        {
            throw new BadRequestException("The kitchen change requires a released order and kitchen destination.");
        }

        var text = summary.Trim();
        if (text.Length == 0 || text.Length > MaximumSummaryLength)
        {
            throw new BadRequestException("A concise kitchen change summary is required.");
        }

        var payload = KitchenChangeSnapshot.Serialize(changes);
        var jobId = CreateJobId(order.Id, amendmentId, target);
        var existing = _context.OrderOperationalNotes.Local.SingleOrDefault(note => note.Id == jobId)
            ?? await _context.OrderOperationalNotes.AsNoTracking()
                .SingleOrDefaultAsync(note => note.Id == jobId, cancellationToken);
        if (existing is not null)
        {
            if (existing.OrderId != order.Id || existing.AmendmentId != amendmentId
                || existing.KitchenTarget != target || existing.AccountRevision != accountRevision
                || existing.Text != text || existing.KitchenChangesJson is null
                || !JsonNode.DeepEquals(JsonNode.Parse(existing.KitchenChangesJson), JsonNode.Parse(payload)))
            {
                throw new BadRequestException("The kitchen change identity already belongs to a different payload.");
            }

            return jobId;
        }

        _context.OrderOperationalNotes.Add(new OrderOperationalNote
        {
            Id = jobId,
            OrderId = order.Id,
            Order = order,
            ClientOperationId = jobId,
            Audience = OrderNoteAudience.Kitchen,
            Text = text,
            AmendmentId = amendmentId,
            AccountRevision = accountRevision,
            KitchenTarget = target,
            KitchenChangesJson = payload,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = _currentUser.GetAuditIdentifier()
        });
        return jobId;
    }

    private static Guid CreateJobId(Guid orderId, Guid amendmentId, DevicePrintTarget target)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"kitchen-amendment:{orderId:N}:{amendmentId:N}:{target}"));
        return new Guid(bytes[..16]);
    }
}

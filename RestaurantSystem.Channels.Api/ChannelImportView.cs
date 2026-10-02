using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using RestaurantSystem.Channels.Domain;

namespace RestaurantSystem.Channels.Api;

public sealed class ChannelImportView(IOptions<TenantBridgeSettings> settings, IOptions<UberWebhookSettings> webhook,
    IChannelImportStatus repository, TimeProvider clock) : IChannelImportView
{
    private const int MaximumCursorLength = 350;

    public async Task<JsonElement> Read(string cursor, CancellationToken cancellationToken)
    {
        var options = settings.Value;
        if (!options.Enabled) return ProviderJson.Encode(new { enabled = false, paused = options.Paused, truncated = false, items = Array.Empty<object>() });
        if (webhook.Value.StoreIds.Length != 1 || webhook.Value.StoreIds[0] != options.Store.StoreId)
            throw new ChannelConsoleException(409, "Import status requires the approved sandbox store binding.");
        var rows = await repository.Read(webhook.Value.ClientId, options.Store.StoreId, options.Store.TenantId, Decode(cursor), cancellationToken);
        return ProviderJson.Encode(new
        {
            enabled = true,
            tenantUrl = options.Store.BaseUrl,
            paused = options.Paused,
            checkedAt = clock.GetUtcNow(),
            truncated = rows.Count > IChannelImportStatus.PageSize,
            nextCursor = rows.Count > IChannelImportStatus.PageSize ? Encode(rows[IChannelImportStatus.PageSize - 1]) : null,
            items = rows.Take(IChannelImportStatus.PageSize).Select(row => new
            {
                orderId = row.OrderId,
                state = row.State,
                code = row.Code,
                attempts = row.Attempts,
                catalogueRevision = row.CatalogueRevision,
                tenantOrderId = row.TenantOrderId,
                createdAt = row.CreatedAt,
                updatedAt = row.UpdatedAt,
                deliveryConfirmed = row.State == "Imported" && row.TenantOrderId is not null,
                reviewRequired = row.State == "Quarantined",
                retrying = row.State != "Quarantined" && row.Code.Length > 0,
            }),
        });
    }

    private static string Encode(ChannelImportStatus row)
        => WebEncoders.Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(new ChannelImportCursor(row.Priority, row.CreatedAt, row.OrderId)));

    private static ChannelImportCursor? Decode(string cursor)
    {
        if (cursor.Length == 0) return null;
        if (cursor.Length > MaximumCursorLength) throw InvalidCursor();
        try
        {
            var decoded = JsonSerializer.Deserialize<ChannelImportCursor>(WebEncoders.Base64UrlDecode(cursor));
            if (decoded is null || decoded.Priority is < 0 or > 3 || decoded.CreatedAt == default || decoded.OrderId == Guid.Empty)
                throw InvalidCursor();
            return decoded;
        }
        catch (FormatException) { throw InvalidCursor(); }
        catch (JsonException) { throw InvalidCursor(); }
    }

    private static ChannelConsoleException InvalidCursor() => new(400, "Refresh import status to start a valid review page.");

}

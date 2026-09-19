using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Orders.Services;

public static class StaffOrderOperationFingerprint
{
    public static string Create(StaffCounterOrderRequest request, bool releaseToKitchen)
    {
        var payload = new
        {
            request.Type,
            request.TableId,
            request.TableNumber,
            request.ServiceSessionId,
            CustomerUserId = request.EffectiveCustomerUserId,
            request.CustomerName,
            request.CustomerEmail,
            request.CustomerPhone,
            request.PromoCode,
            request.PointsToRedeem,
            Tip = request.Tip ?? 0m,
            request.Notes,
            request.PaymentState,
            // Part of the payload: two creates sharing one operation id but shipping different
            // addresses are different operations, and only the hash can tell them apart.
            request.DeliveryAddress,
            request.Items,
            releaseToKitchen
        };
        return Hash(JsonSerializer.Serialize(payload));
    }

    public static string CreateRelease(Guid orderId, int expectedVersion) =>
        Hash(JsonSerializer.Serialize(new { orderId, expectedVersion, Kind = StaffOrderOperationKind.Release }));

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

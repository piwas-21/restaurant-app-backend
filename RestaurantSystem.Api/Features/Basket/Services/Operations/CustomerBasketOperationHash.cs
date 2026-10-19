using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using RestaurantSystem.Api.Common.Exceptions;

namespace RestaurantSystem.Api.Features.Basket.Services.Operations;

internal static class CustomerBasketOperationHash
{
    public static Guid Session(string sessionId)
    {
        if (!Guid.TryParseExact(sessionId, "D", out var session) || session == Guid.Empty
            || !string.Equals(sessionId, session.ToString("D"), StringComparison.Ordinal))
            throw new BadRequestException("A canonical non-empty UUID session is required for recoverable basket operations.");
        return session;
    }

    public static string Owner(Guid session, Guid? userId) =>
        Digest(JsonSerializer.SerializeToUtf8Bytes(new { Session = session, User = userId }));

    public static long OwnerLock(Guid session, Guid? userId) =>
        Lock("customer-basket-owner", userId.HasValue ? $"user:{userId:D}" : $"guest:{session:D}");

    public static long OperationLock(Guid operationId) => Lock("customer-basket-operation", operationId.ToString("D"));

    public static string Payload<T>(T item)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
            WriteCanonical(writer, JsonSerializer.SerializeToElement(item));
        return Digest(stream.ToArray());
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            foreach (var property in element.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
            {
                writer.WritePropertyName(property.Name);
                WriteCanonical(writer, property.Value);
            }
            writer.WriteEndObject();
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray();
            foreach (var value in element.EnumerateArray()) WriteCanonical(writer, value);
            writer.WriteEndArray();
        }
        else
        {
            element.WriteTo(writer);
        }
    }

    private static long Lock(string scope, string value) =>
        BinaryPrimitives.ReadInt64LittleEndian(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { scope, value })));

    private static string Digest(byte[] value) => Convert.ToHexStringLower(SHA256.HashData(value));
}

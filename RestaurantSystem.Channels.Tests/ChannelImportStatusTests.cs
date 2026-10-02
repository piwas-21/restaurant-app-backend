using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;
using RestaurantSystem.Channels.Api;
using RestaurantSystem.Channels.Domain;
using RestaurantSystem.Channels.Infrastructure;

namespace RestaurantSystem.Channels.Tests;

[Collection("Channel gateway")]
public sealed class ChannelImportStatusTests(GatewayFixture fixture)
{
    private readonly string _clientId = Guid.NewGuid().ToString();
    private static readonly DateTimeOffset Created = new(2026, 10, 2, 4, 0, 0, TimeSpan.Zero);
    private static readonly Guid First = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid Second = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private const string PrivateMarker = "private-fixture-token";
    private static TenantBridgeSettings Settings() => new()
    {
        Enabled = true,
        Store = new() { StoreId = GatewayFixture.StoreId, TenantId = Guid.NewGuid().ToString() },
    };
    private ChannelImportView View(TenantBridgeSettings settings, IChannelImportStatus repository)
        => new(Options.Create(settings), Options.Create(new UberWebhookSettings
        {
            ClientId = _clientId,
            StoreIds = [GatewayFixture.StoreId]
        }), repository, TimeProvider.System);

    private async Task Insert(NpgsqlDataSource source, string tenant, Guid id, string state = "Quarantined",
        string code = "ContractRejected", string? client = null, Guid? store = null, string revision = "reviewed-v1")
    {
        await using var command = source.CreateCommand("""
            INSERT INTO channel_import_jobs (client_id, store_id, order_id, tenant_id, catalogue_revision, state,
                last_code, tenant_order_id, created_at, updated_at, encrypted_request, request_hash, payload_expires_at)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $9, 'private-fixture-cipher', repeat('a', 64), now() + interval '1 day')
            """);
        command.Parameters.AddWithValue(client ?? _clientId); command.Parameters.AddWithValue(store ?? GatewayFixture.StoreId);
        command.Parameters.AddWithValue(id); command.Parameters.AddWithValue(tenant); command.Parameters.AddWithValue(revision);
        command.Parameters.AddWithValue(state); command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Text, Value = code.Length == 0 ? DBNull.Value : code });
        command.Parameters.Add(new NpgsqlParameter
        {
            NpgsqlDbType = NpgsqlDbType.Uuid,
            Value = state == "Imported" ? Guid.NewGuid() : DBNull.Value
        });
        command.Parameters.AddWithValue(Created); await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task BoundReviewIncludesOldRevisionButNeverForeignClientStoreOrTenantOrPrivatePayload()
    {
        await using var source = NpgsqlDataSource.Create(fixture.ConnectionString); var settings = Settings();
        await Insert(source, settings.Store.TenantId, First, code: PrivateMarker);
        await Insert(source, settings.Store.TenantId, Second, revision: "older-revision");
        await Insert(source, settings.Store.TenantId, Guid.NewGuid(), client: "foreign-client");
        await Insert(source, settings.Store.TenantId, Guid.NewGuid(), store: Guid.NewGuid());
        await Insert(source, "foreign-tenant", Guid.NewGuid());
        var body = await View(settings, new PostgresChannelImportStatus(source)).Read("", default);
        var rows = body.GetProperty("items"); Assert.Equal(2, rows.GetArrayLength());
        Assert.Equal(First, rows[0].GetProperty("orderId").GetGuid()); Assert.Equal(Second, rows[1].GetProperty("orderId").GetGuid());
        Assert.Equal("ReviewRequired", rows[0].GetProperty("code").GetString());
        Assert.Equal("older-revision", rows[1].GetProperty("catalogueRevision").GetString());
        Assert.All(rows.EnumerateArray(), row => Assert.True(row.GetProperty("reviewRequired").GetBoolean()));
        Assert.DoesNotContain(PrivateMarker, body.GetRawText()); Assert.DoesNotContain("private-fixture-cipher", body.GetRawText());
        Assert.False(body.GetProperty("truncated").GetBoolean());
    }

    [Fact]
    public async Task ReviewPriorityAndStableCursorCoverEveryJobWithoutDuplicatingTiedRows()
    {
        await using var source = NpgsqlDataSource.Create(fixture.ConnectionString); var settings = Settings();
        var expected = new HashSet<Guid> { First, Second };
        await Insert(source, settings.Store.TenantId, First);
        await Insert(source, settings.Store.TenantId, Second, "Prepared", "DeliveryUncertain");
        for (var i = 0; i < 51; i++) { var id = Guid.NewGuid(); expected.Add(id); await Insert(source, settings.Store.TenantId, id, "Imported", ""); }
        var view = View(settings, new PostgresChannelImportStatus(source)); var first = await view.Read("", default);
        var rows = first.GetProperty("items"); Assert.Equal(50, rows.GetArrayLength());
        Assert.Equal(First, rows[0].GetProperty("orderId").GetGuid()); Assert.True(rows[0].GetProperty("reviewRequired").GetBoolean());
        Assert.Equal(Second, rows[1].GetProperty("orderId").GetGuid()); Assert.True(rows[1].GetProperty("retrying").GetBoolean());
        Assert.False(rows[1].GetProperty("reviewRequired").GetBoolean()); Assert.True(first.GetProperty("truncated").GetBoolean());
        var cursor = first.GetProperty("nextCursor").GetString()!; var second = await view.Read(cursor, default);
        Assert.Equal(3, second.GetProperty("items").GetArrayLength()); Assert.False(second.GetProperty("truncated").GetBoolean());
        var seen = rows.EnumerateArray().Concat(second.GetProperty("items").EnumerateArray()).Select(row => row.GetProperty("orderId").GetGuid()).ToArray();
        Assert.Equal(53, seen.Distinct().Count()); Assert.True(expected.SetEquals(seen));
    }

    [Theory]
    [InlineData("not-valid-cursor")]
    [InlineData("e30")]
    [InlineData("a")]
    public async Task InvalidCursorCannotSelectAnUnboundedOrForeignPage(string cursor)
    {
        await using var source = NpgsqlDataSource.Create(fixture.ConnectionString); var settings = Settings();
        var error = await Assert.ThrowsAsync<ChannelConsoleException>(() => View(settings, new PostgresChannelImportStatus(source)).Read(cursor, default));
        Assert.Equal(400, error.Status);
    }

    [Fact]
    public async Task CursorWithNonUtcOffsetSelectsTheSameInstantInPostgres()
    {
        await using var source = NpgsqlDataSource.Create(fixture.ConnectionString); var settings = Settings();
        await Insert(source, settings.Store.TenantId, First);
        await Insert(source, settings.Store.TenantId, Second);
        var cursor = WebEncoders.Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(
            new ChannelImportCursor(0, Created.ToOffset(TimeSpan.FromHours(2)), First)));
        var body = await View(settings, new PostgresChannelImportStatus(source)).Read(cursor, default);
        var row = Assert.Single(body.GetProperty("items").EnumerateArray());
        Assert.Equal(Second, row.GetProperty("orderId").GetGuid());
        Assert.False(body.GetProperty("truncated").GetBoolean());
    }

    [Fact]
    public async Task DisabledViewNeverTouchesRepositoryAndUnapprovedStoreCannotRead()
    {
        var repository = new RefusingRepository(); var settings = Settings(); settings.Enabled = false;
        var disabled = await View(settings, repository).Read("", default); Assert.False(disabled.GetProperty("enabled").GetBoolean());
        Assert.Equal(0, disabled.GetProperty("items").GetArrayLength());
        settings.Enabled = true; settings.Store.StoreId = Guid.NewGuid();
        var error = await Assert.ThrowsAsync<ChannelConsoleException>(() => View(settings, repository).Read("", default));
        Assert.Equal(409, error.Status);
    }

    private sealed class RefusingRepository : IChannelImportStatus
    {
        public Task<IReadOnlyList<ChannelImportStatus>> Read(string clientId, Guid storeId, string tenantId,
            ChannelImportCursor? cursor, CancellationToken cancellationToken) => throw new Xunit.Sdk.XunitException("Unexpected repository access");
    }
}

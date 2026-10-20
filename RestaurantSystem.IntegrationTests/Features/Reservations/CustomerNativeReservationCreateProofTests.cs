using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Features.Reservations.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Features.Basket;

namespace RestaurantSystem.IntegrationTests.Features.Reservations;

public sealed partial class CustomerNativeReservationCreateTests
{
    [Theory]
    [InlineData("guest-customer")]
    [InlineData("customer-guest")]
    [InlineData("customer-other")]
    [InlineData("session")]
    public async Task Unseen_proof_cannot_be_rebound_to_another_principal_or_session(string mode)
    {
        var signedIn = mode.StartsWith("customer", StringComparison.Ordinal);
        if (signedIn) Authenticate();
        var proof = await PrepareAsync(Request());
        var foreignSession = _session;
        if (mode == "guest-customer") Authenticate();
        else if (mode == "customer-guest") Client.DefaultRequestHeaders.Authorization = null;
        else if (mode == "customer-other")
        {
            var other = Guid.NewGuid();
            await using var seed = DatabaseFixture.CreateContext();
            seed.Users.Add(new ApplicationUser
            {
                Id = other,
                UserName = "another-customer",
                Email = "another@example.test",
                FirstName = "Other",
                LastName = "Customer",
                Role = UserRole.Customer,
                CreatedBy = "test",
                RefreshToken = ""
            });
            await seed.SaveChangesAsync();
            Client.DefaultRequestHeaders.Authorization = new("Bearer",
                CustomerBasketTokens.Create(Factory.Services, UserRole.Customer, userId: other.ToString("D")));
        }
        else foreignSession = Guid.NewGuid().ToString("D");
        foreach (var path in new[] { "/recover", "/cancel-attempt" })
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, Route + path);
            message.Headers.Add("X-Session-Id", foreignSession);
            message.Content = JsonContent.Create(proof);
            Assert.Equal(HttpStatusCode.NotFound, (await Client.SendAsync(message)).StatusCode);
        }
        await using var db = DatabaseFixture.CreateContext();
        Assert.Empty(await db.Reservations.ToListAsync()); Assert.Empty(await db.Set<CustomerReservationOperation>().ToListAsync());
        if (signedIn) Authenticate(); else Client.DefaultRequestHeaders.Authorization = null;
        Mobile.Enabled = false;
        Assert.Equal("Unseen", (await ResolveProofAsync(proof, false)).GetProperty("outcome").GetString());
        Assert.Equal("Cancelled", (await ResolveProofAsync(proof, true)).GetProperty("outcome").GetString());
        Assert.Equal(0, _mailCalls);
    }

    [Theory]
    [InlineData("operation")]
    [InlineData("hash")]
    [InlineData("tenant")]
    [InlineData("token")]
    public async Task Protected_preparation_fields_cannot_be_changed_before_any_receipt_exists(string mode)
    {
        var proof = await PrepareAsync(Request());
        var corrupted = proof.OwnerProof.ToCharArray();
        corrupted[corrupted.Length / 2] = corrupted[corrupted.Length / 2] == 'A' ? 'B' : 'A';
        var changed = mode switch
        {
            "operation" => proof with { OperationId = Guid.NewGuid() },
            "hash" => proof with { PayloadHash = new string(proof.PayloadHash[0] == 'a' ? 'b' : 'a', 64) },
            "tenant" => proof with { TenantId = Guid.NewGuid() },
            _ => proof with { OwnerProof = new string(corrupted) }
        };
        foreach (var path in new[] { "/recover", "/cancel-attempt" })
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, Route + path);
            message.Headers.Add("X-Session-Id", _session); message.Content = JsonContent.Create(changed);
            Assert.Equal(HttpStatusCode.NotFound, (await Client.SendAsync(message)).StatusCode);
        }
        await using var db = DatabaseFixture.CreateContext();
        Assert.Empty(await db.Set<CustomerReservationOperation>().ToListAsync());
        Assert.Equal("Cancelled", (await ResolveProofAsync(proof, true)).GetProperty("outcome").GetString());
        Assert.Empty(await db.Reservations.ToListAsync()); Assert.Equal(0, _mailCalls);
    }
    [Theory]
    [InlineData("/prepare", "operationId")]
    [InlineData("/prepare", "tenantId")]
    [InlineData("", "operationId")]
    [InlineData("", "tenantId")]
    [InlineData("/recover", "operationId")]
    [InlineData("/recover", "tenantId")]
    [InlineData("/cancel-attempt", "operationId")]
    [InlineData("/cancel-attempt", "tenantId")]
    public async Task Omitted_required_identity_fields_are_refused_before_booking_or_sealing(string suffix, string field)
    {
        var request = Request(); var proof = await PrepareAsync(request);
        object body = suffix switch
        {
            "/prepare" => request,
            "" => request with { PayloadHash = proof.PayloadHash, OwnerProof = proof.OwnerProof },
            _ => proof
        };
        var payload = Assert.IsType<JsonObject>(JsonSerializer.SerializeToNode(body, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.True(payload.Remove(field));
        using var message = new HttpRequestMessage(HttpMethod.Post, Route + suffix);
        message.Headers.Add("X-Session-Id", _session); message.Content = JsonContent.Create(payload);
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.SendAsync(message)).StatusCode);
        await using var verify = DatabaseFixture.CreateContext();
        Assert.Empty(await verify.Reservations.ToListAsync()); Assert.Empty(await verify.Set<CustomerReservationOperation>().ToListAsync());
        Assert.Equal("Unseen", (await ResolveProofAsync(proof, false)).GetProperty("outcome").GetString());
        Assert.Equal("Applied", (await ResolveAsync(request)).GetProperty("outcome").GetString());
        Assert.Single(await verify.Reservations.ToListAsync()); Assert.Single(await verify.Set<CustomerReservationOperation>().ToListAsync());
    }

}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Reservations.Dtos;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.IntegrationTests.Features.Reservations;

public sealed partial class CustomerNativeReservationCreateTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Preparation_and_unseen_recovery_have_no_effect_and_minimal_proof_can_seal_without_contacts(bool signedIn)
    {
        if (signedIn) Authenticate();
        var request = Request(); var proof = await PrepareAsync(request);
        await using var db = DatabaseFixture.CreateContext();
        Assert.Empty(await db.Reservations.ToListAsync()); Assert.Empty(await db.Set<CustomerReservationOperation>().ToListAsync());
        Mobile.Enabled = false;
        var unseen = await ResolveProofAsync(proof, false);
        Assert.Equal("Unseen", unseen.GetProperty("outcome").GetString());
        Assert.Empty(await db.Set<CustomerReservationOperation>().ToListAsync());
        var sealedAttempt = await ResolveProofAsync(proof, true);
        Assert.Equal("Cancelled", sealedAttempt.GetProperty("outcome").GetString());
        Assert.Equal(sealedAttempt.GetRawText(), (await ResolveProofAsync(proof, false)).GetRawText());
        Mobile.Enabled = true;
        Assert.Equal(sealedAttempt.GetRawText(), (await ResolveAsync(request)).GetRawText());
        Assert.Empty(await db.Reservations.ToListAsync()); Assert.Equal(0, _mailCalls);
    }

    [Fact]
    public async Task Applied_recovery_needs_only_original_minimal_proof_after_contacts_are_discarded()
    {
        var request = Request(); var proof = await PrepareAsync(request);
        var accepted = await ResolveAsync(request);
        Mobile.Enabled = false;
        Assert.Equal(accepted.GetRawText(), (await ResolveProofAsync(proof, false)).GetRawText());
        Assert.Equal(accepted.GetRawText(), (await ResolveProofAsync(proof, true)).GetRawText());
        Assert.Equal(1, _mailCalls);
    }

    [Fact]
    public async Task A_body_changed_after_preparation_is_refused_without_rebinding_its_original_hash()
    {
        var original = Request(); var proof = await PrepareAsync(original);
        Assert.Equal(HttpStatusCode.Conflict, (await SendAsync(original with
        { CustomerName = "Changed after preparation", PayloadHash = proof.PayloadHash, OwnerProof = proof.OwnerProof })).StatusCode);
        await using var db = DatabaseFixture.CreateContext();
        Assert.Empty(await db.Reservations.ToListAsync()); Assert.Empty(await db.Set<CustomerReservationOperation>().ToListAsync());
        Assert.Equal("Applied", (await ResolveAsync(original)).GetProperty("outcome").GetString());
    }

    [Fact]
    public async Task Preparation_hash_binds_the_original_session_and_principal_before_any_create()
    {
        var original = Request(); var proof = await PrepareAsync(original);
        Authenticate();
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(original with { PayloadHash = proof.PayloadHash, OwnerProof = proof.OwnerProof })).StatusCode);
        Client.DefaultRequestHeaders.Authorization = null;
        Assert.Equal("Applied", (await ResolveAsync(original)).GetProperty("outcome").GetString());
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("operation")]
    [InlineData("tenant")]
    [InlineData("hash")]
    [InlineData("proof")]
    public async Task Invalid_minimal_proofs_cannot_recover_or_seal_and_leave_no_receipt(string mode)
    {
        var proof = await PrepareAsync(Request());
        var invalid = mode switch
        {
            "schema" => proof with { SchemaVersion = null },
            "operation" => proof with { OperationId = Guid.Empty },
            "tenant" => proof with { TenantId = Guid.Empty },
            "hash" => proof with { PayloadHash = "invalid" },
            _ => proof with { OwnerProof = "" }
        };
        foreach (var path in new[] { "/recover", "/cancel-attempt" })
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, Route + path);
            message.Headers.Add("X-Session-Id", _session); message.Content = JsonContent.Create(invalid);
            Assert.Equal(HttpStatusCode.BadRequest, (await Client.SendAsync(message)).StatusCode);
        }
        await using var db = DatabaseFixture.CreateContext();
        Assert.Empty(await db.Set<CustomerReservationOperation>().ToListAsync());
        Assert.Equal("Unseen", (await ResolveProofAsync(proof, false)).GetProperty("outcome").GetString());
        Assert.Equal("Cancelled", (await ResolveProofAsync(proof, true)).GetProperty("outcome").GetString());
        Assert.Empty(await db.Reservations.ToListAsync()); Assert.Equal(0, _mailCalls);
    }

    private async Task<JsonElement> ResolveProofAsync(CustomerReservationProofDto proof, bool cancel)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, Route + (cancel ? "/cancel-attempt" : "/recover"));
        message.Headers.Add("X-Session-Id", _session); message.Content = JsonContent.Create(proof);
        using var response = await Client.SendAsync(message);
        var text = await response.Content.ReadAsStringAsync(); Assert.True(response.StatusCode == HttpStatusCode.OK, text);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.DoesNotContain("customerEmail", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Customer request", text, StringComparison.Ordinal);
        var result = (await response.Content.ReadFromJsonAsync<ApiResponse<JsonElement>>())!;
        Assert.True(result.Success); return result.Data;
    }
}

using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.TableGuestVisits;

[Collection("Database Lane 3")]
public sealed class CustomerTablePaymentWireTests(DatabaseFixture fixture) : CustomerTablePaymentPreparationFixture(fixture)
{
    private const string Path = "/api/customer-mobile/table-payments/";

    [Theory]
    [InlineData("intent")]
    [InlineData("proof")]
    [InlineData("unknown")]
    public async Task Null_or_extra_submission_fields_are_refused_without_decision(string changed)
    {
        var intent = await PaymentIntentAsync(); var prepared = await PreparedPaymentAsync(intent);
        var body = JsonSerializer.SerializeToNode(new CustomerTablePaymentSubmissionDto
        { Intent = intent, Proof = prepared.Proof }, JsonOptions)!.AsObject();
        if (changed == "unknown") body["redirectUrl"] = "https://untrusted.example.test";
        else body[changed] = null;
        (await SendAsync(Path + "submit", body)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await using var context = DatabaseFixture.CreateContext();
        (await context.Set<CustomerTablePaymentOperation>().CountAsync()).Should().Be(0);
        await NoPaymentEffectsAsync();
    }

    [Theory]
    [InlineData("schemaVersion")]
    [InlineData("tenantId")]
    [InlineData("operationId")]
    [InlineData("payloadHash")]
    [InlineData("ownerProof")]
    [InlineData("extra")]
    public async Task Recovery_proof_requires_every_original_field_and_refuses_extra_fields(string removed)
    {
        var intent = await PaymentIntentAsync(); var prepared = await PreparedPaymentAsync(intent);
        var body = JsonSerializer.SerializeToNode(prepared.Proof, JsonOptions)!.AsObject();
        if (removed == "extra") body["amountMinor"] = 1; else body.Remove(removed);
        (await SendAsync(Path + "recover", body)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await SendAsync(Path + "stop", body)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await using var context = DatabaseFixture.CreateContext();
        (await context.Set<CustomerTablePaymentOperation>().CountAsync()).Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Customer_or_invalid_bearer_cannot_recover_a_guest_original_as_guest(bool invalid)
    {
        var intent = await PaymentIntentAsync(); var prepared = await PreparedPaymentAsync(intent);
        if (invalid) Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "invalid");
        else await SignInCustomerAsync();
        (await SendAsync(Path + "recover", prepared.Proof)).IsSuccessStatusCode.Should().BeFalse();
        (await SendAsync(Path + "stop", prepared.Proof)).IsSuccessStatusCode.Should().BeFalse();
        await using var context = DatabaseFixture.CreateContext();
        (await context.Set<CustomerTablePaymentOperation>().CountAsync()).Should().Be(0);
        await NoPaymentEffectsAsync();
    }
}

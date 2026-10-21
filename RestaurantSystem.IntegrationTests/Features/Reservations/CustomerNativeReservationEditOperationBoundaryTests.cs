using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Reservations.Dtos;
using RestaurantSystem.Api.Features.Reservations.Services;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Common;

namespace RestaurantSystem.IntegrationTests.Features.Reservations;

public sealed partial class CustomerNativeReservationEditOperationTests
{
    [Theory]
    [InlineData("operationId")]
    [InlineData("tenantId")]
    [InlineData("reservationId")]
    public async Task Missing_write_identifiers_never_bind_as_valid_default_UUIDs(string field)
    {
        Authenticate(); var node = JsonSerializer.SerializeToNode(Request(), JsonOptions)!.AsObject(); Assert.True(node.Remove(field));
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync("prepare", node)).StatusCode);
        await using var db = DatabaseFixture.CreateContext(); Assert.Empty(await db.Set<CustomerReservationOperation>().ToListAsync());
    }
    [Theory]
    [InlineData("foreign")]
    [InlineData("guest")]
    [InlineData("missing")]
    public async Task Foreign_guest_and_missing_bookings_cannot_prepare_an_edit_or_disclose_contacts(string mode)
    {
        Authenticate(); await using var db = DatabaseFixture.CreateContext();
        if (mode != "missing")
        {
            Guid? owner = mode == "guest" ? null : Guid.Parse(TestAuthHandler.AdminUserId);
            await db.Reservations.ExecuteUpdateAsync(update => update.SetProperty(row => row.CustomerId, owner));
        }
        var request = mode == "missing" ? Request() with { ReservationId = Guid.NewGuid() } : Request();
        using var response = await SendAsync("prepare", request); Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("editor@example.test", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Empty(await db.Set<CustomerReservationOperation>().ToListAsync()); Assert.Equal(0, _mailCalls);
    }
    [Theory]
    [InlineData("create")]
    [InlineData("cancellation")]
    public async Task Preparation_proofs_are_not_interchangeable_between_creation_cancellation_and_edits(string kind)
    {
        Authenticate(); var (_, edit) = await PrepareAsync();
        async Task<HttpResponseMessage> SendOther(string path, object body)
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, "/api/customer-mobile/reservations/" + kind + "/" + path);
            message.Headers.Add("X-Session-Id", _session); message.Content = JsonContent.Create(body); return await Client.SendAsync(message);
        }
        using var wrong = await SendOther("recover", edit.Proof); Assert.Equal(HttpStatusCode.NotFound, wrong.StatusCode);
        object input = kind == "create" ? CustomerReservationEditInput.ToCreate(Request())
            : new CustomerReservationCancellationDto(1, Guid.NewGuid(), Tenant, _booking, "Europe/Zurich");
        using var prepared = await SendOther("prepare", input);
        Assert.True(prepared.StatusCode == HttpStatusCode.OK, await prepared.Content.ReadAsStringAsync());
        var body = await prepared.Content.ReadFromJsonAsync<ApiResponse<JsonElement>>(); Assert.NotNull(body); Assert.True(body.Success);
        var proofNode = kind == "create" ? body.Data : body.Data.GetProperty("proof");
        var other = proofNode.Deserialize<CustomerReservationProofDto>(JsonOptions); Assert.NotNull(other);
        foreach (var path in new[] { "recover", "cancel-attempt" })
            Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(path, other)).StatusCode);
        await using var db = DatabaseFixture.CreateContext(); Assert.Empty(await db.Set<CustomerReservationOperation>().ToListAsync()); Assert.Equal(0, _mailCalls);
    }
    [Fact]
    public async Task Minimal_recovery_refuses_full_contact_input_without_creating_a_decision()
    {
        Authenticate(); var (request, _) = await PrepareAsync();
        foreach (var path in new[] { "recover", "cancel-attempt" }) Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(path, request)).StatusCode);
        await using var db = DatabaseFixture.CreateContext(); Assert.Empty(await db.Set<CustomerReservationOperation>().ToListAsync()); Assert.Equal(0, _mailCalls);
    }
}

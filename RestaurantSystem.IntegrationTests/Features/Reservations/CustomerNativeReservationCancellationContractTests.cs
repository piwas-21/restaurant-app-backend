using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Features.Reservations.Dtos;
using RestaurantSystem.Api.Features.Basket.Services.Operations;
using RestaurantSystem.Api.Features.Reservations.Services;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.IntegrationTests.Features.Reservations;

public sealed partial class CustomerNativeReservationCancellationTests
{
    [Theory]
    [InlineData("schemaVersion")]
    [InlineData("operationId")]
    [InlineData("tenantId")]
    [InlineData("reservationId")]
    [InlineData("timeZone")]
    [InlineData("extra")]
    public async Task Preparation_requires_every_supported_field_and_refuses_unreviewed_staff_fields(string mode)
    {
        Authenticate(); var request = Request();
        var body = new Dictionary<string, object?>
        {
            ["schemaVersion"] = 1,
            ["operationId"] = request.OperationId,
            ["tenantId"] = Tenant,
            ["reservationId"] = _booking,
            ["timeZone"] = "Europe/Zurich"
        };
        if (mode == "extra") body["status"] = "Cancelled"; else body.Remove(mode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync("prepare", body)).StatusCode);
        await AssertUntouchedAsync();
    }
    [Theory]
    [InlineData("schemaVersion")]
    [InlineData("operationId")]
    [InlineData("tenantId")]
    [InlineData("payloadHash")]
    [InlineData("ownerProof")]
    [InlineData("extra")]
    public async Task Every_resolution_action_requires_complete_minimal_proof_and_refuses_extra_fields(string mode)
    {
        Authenticate(); var proof = (await PrepareAsync()).Proof;
        var body = new Dictionary<string, object?>
        {
            ["schemaVersion"] = 1,
            ["operationId"] = proof.OperationId,
            ["tenantId"] = Tenant,
            ["payloadHash"] = proof.PayloadHash,
            ["ownerProof"] = proof.OwnerProof
        };
        if (mode == "extra") body["reservationId"] = _booking; else body.Remove(mode);
        foreach (var path in new[] { "submit", "recover", "cancel-attempt" })
            Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(path, body)).StatusCode);
        await AssertUntouchedAsync();
    }
    [Fact]
    public async Task Creation_and_cancellation_protected_purposes_cannot_be_used_in_each_others_routes()
    {
        Authenticate(); var cancellation = (await PrepareAsync()).Proof;
        using var scope = Factory.Services.CreateScope();
        var owner = new CustomerReservationOperationOwner(Owner,
            CustomerBasketOperationHash.Owner(Guid.Parse(_session), Owner), cancellation.PayloadHash);
        var creator = scope.ServiceProvider.GetRequiredService<ICustomerReservationProofService>();
        var create = cancellation with { OwnerProof = creator.Create(Tenant, cancellation.OperationId, owner) };
        foreach (var path in new[] { "submit", "recover", "cancel-attempt" })
            Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(path, create)).StatusCode);
        // The original create service also rejects a cancellation-purpose proof before any database write.
        var exception = Record.Exception(() => creator.Require(cancellation, owner));
        Assert.IsType<RestaurantSystem.Api.Common.Exceptions.NotFoundException>(exception);
        await AssertUntouchedAsync();
    }
    [Fact]
    public async Task Machine_credentials_cannot_substitute_for_an_authenticated_customer()
    {
        using var scope = Factory.Services.CreateScope();
        await using var db = DatabaseFixture.CreateContext();
        var current = new Moq.Mock<RestaurantSystem.Api.Common.Services.Interfaces.ICurrentUserService>();
        current.SetupGet(value => value.IsAuthenticated).Returns(true);
        current.SetupGet(value => value.UserId).Returns(Owner);
        current.SetupGet(value => value.Role).Returns(RestaurantSystem.Domain.Common.Enums.UserRole.Customer);
        current.SetupGet(value => value.IsApiToken).Returns(true);
        var access = ActivatorUtilities.CreateInstance<CustomerReservationCancellationAccess>(scope.ServiceProvider, db, current.Object);
        Assert.Throws<RestaurantSystem.Api.Common.Exceptions.ForbiddenException>(() => access.Capture(_session, 1, Guid.NewGuid(), Tenant));
        await AssertUntouchedAsync();
    }
    [Fact]
    public async Task An_existing_operation_id_cannot_be_rebound_to_a_different_review_of_the_same_booking()
    {
        Authenticate(); var request = Request(); var first = (await PrepareAsync(request)).Proof;
        Assert.Equal("Cancelled", (await DecideAsync("cancel-attempt", first)).GetProperty("outcome").GetString());
        await using var db = DatabaseFixture.CreateContext();
        await db.Reservations.ExecuteUpdateAsync(update => update.SetProperty(row => row.CustomerPhone, "Changed"));
        var second = (await PrepareAsync(request)).Proof;
        Assert.NotEqual(first.PayloadHash, second.PayloadHash);
        Assert.Equal(HttpStatusCode.Conflict, (await SendAsync("submit", second)).StatusCode);
        Assert.Equal("Cancelled", (await DecideAsync("recover", first)).GetProperty("outcome").GetString());
        Assert.Single(await db.Set<CustomerReservationOperation>().ToListAsync()); Assert.Equal(0, _mailCalls);
    }
}

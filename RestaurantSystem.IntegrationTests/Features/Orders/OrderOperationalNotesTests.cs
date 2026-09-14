using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

/// <summary>
/// Pins operational notes to their own internal, append-only contract. In particular, a green UI
/// toast is not proof: each persistence assertion reads a fresh HTTP resource or database scope.
/// </summary>
[Collection("Database Lane 4")]
public class OrderOperationalNotesTests : IntegrationTestBase
{
    private Guid _orderId;
    private Guid _otherOrderId;

    public OrderOperationalNotesTests(DatabaseFixture databaseFixture) : base(databaseFixture)
    {
    }

    [Fact]
    public async Task Cashier_note_survives_a_fresh_get_with_server_authored_metadata()
    {
        AuthenticateAsRole(UserRole.Cashier);
        var operationId = Guid.NewGuid();
        var create = await PostAsJsonAsync($"/api/orders/{_orderId}/notes", new
        {
            text = "  Pack sauces separately  ",
            audience = "Kitchen",
            clientOperationId = operationId,
            createdBy = "forged-client-identity"
        });
        create.StatusCode.Should().Be(HttpStatusCode.OK);

        var persisted = (await ReadResponseAsync<ApiResponse<OrderOperationalNoteDto>>(create))!;
        persisted.Success.Should().BeTrue();
        persisted.Data!.OrderId.Should().Be(_orderId);
        persisted.Data.Text.Should().Be("Pack sauces separately");
        persisted.Data.CreatedBy.Should().NotBe("forged-client-identity");
        persisted.Data.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));

        var read = await Client.GetAsync($"/api/orders/{_orderId}/notes");
        var notes = (await ReadResponseAsync<ApiResponse<List<OrderOperationalNoteDto>>>(read))!;
        notes.Success.Should().BeTrue();
        notes.Data.Should().ContainSingle(note => note.Id == persisted.Data.Id
            && note.Text == "Pack sauces separately" && note.Audience == "Kitchen");
    }

    [Fact]
    public async Task Retrying_the_same_operation_creates_exactly_one_note()
    {
        AuthenticateAsAdmin();
        var operationId = Guid.NewGuid();
        var body = new { text = "No onions", audience = "Kitchen", clientOperationId = operationId };

        var first = await PostAsJsonAsync($"/api/orders/{_orderId}/notes", body);
        var retry = await PostAsJsonAsync($"/api/orders/{_orderId}/notes", body);
        var firstNote = (await ReadResponseAsync<ApiResponse<OrderOperationalNoteDto>>(first))!.Data!;
        var retriedNote = (await ReadResponseAsync<ApiResponse<OrderOperationalNoteDto>>(retry))!.Data!;

        firstNote.Id.Should().Be(retriedNote.Id);
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await context.OrderOperationalNotes.CountAsync(note => note.OrderId == _orderId
            && note.ClientOperationId == operationId)).Should().Be(1);
    }

    [Fact]
    public async Task Route_order_id_cannot_be_retargeted_by_the_request_body()
    {
        AuthenticateAsAdmin();
        var response = await PostAsJsonAsync($"/api/orders/{_orderId}/notes", new
        {
            orderId = _otherOrderId,
            text = "Route wins",
            audience = "Staff",
            clientOperationId = Guid.NewGuid()
        });

        var note = (await ReadResponseAsync<ApiResponse<OrderOperationalNoteDto>>(response))!.Data!;
        note.OrderId.Should().Be(_orderId);
        var otherNotes = await Client.GetAsync($"/api/orders/{_otherOrderId}/notes");
        (await ReadResponseAsync<ApiResponse<List<OrderOperationalNoteDto>>>(otherNotes))!.Data.Should().BeEmpty();
    }

    [Fact]
    public async Task Customers_cannot_read_or_write_and_kitchen_only_reads_kitchen_notes()
    {
        AuthenticateAsUser();
        (await Client.GetAsync($"/api/orders/{_orderId}/notes")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await PostAsJsonAsync($"/api/orders/{_orderId}/notes", new
        {
            text = "Unauthorized",
            audience = "Kitchen",
            clientOperationId = Guid.NewGuid()
        })).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        AuthenticateAsAdmin();
        await PostAsJsonAsync($"/api/orders/{_orderId}/notes", new
        {
            text = "Kitchen sees this",
            audience = "Kitchen",
            clientOperationId = Guid.NewGuid()
        });
        await PostAsJsonAsync($"/api/orders/{_orderId}/notes", new
        {
            text = "Kitchen must not see this",
            audience = "Staff",
            clientOperationId = Guid.NewGuid()
        });

        AuthenticateAsRole(UserRole.KitchenStaff);
        var kitchenRead = await Client.GetAsync($"/api/orders/{_orderId}/notes");
        var kitchenNotes = (await ReadResponseAsync<ApiResponse<List<OrderOperationalNoteDto>>>(kitchenRead))!.Data!;
        kitchenNotes.Select(note => note.Text).Should().Contain("Kitchen sees this");
        kitchenNotes.Select(note => note.Text).Should().NotContain("Kitchen must not see this");
    }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var orders = new[]
        {
            new Order
            {
                OrderNumber = "POS-NOTES-ONE",
                Type = OrderType.Takeaway,
                Status = OrderStatus.Pending,
                PaymentStatus = PaymentStatus.Pending,
                OrderDate = DateTime.UtcNow,
                Total = 10m,
                CreatedBy = "test"
            },
            new Order
            {
                OrderNumber = "POS-NOTES-TWO",
                Type = OrderType.Takeaway,
                Status = OrderStatus.Pending,
                PaymentStatus = PaymentStatus.Pending,
                OrderDate = DateTime.UtcNow,
                Total = 10m,
                CreatedBy = "test"
            }
        };
        context.Orders.AddRange(orders);
        await context.SaveChangesAsync();
        _orderId = orders[0].Id;
        _otherOrderId = orders[1].Id;
    }
}

using Moq;
using Microsoft.Extensions.Logging.Abstractions;
using RestaurantSystem.Api.Common.Services;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.User.Commands.DeleteUserCommand;
using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

[Collection("Database Lane 4")]
public sealed class CustomerOrderReceiptTests(DatabaseFixture fixture) : CustomerCheckoutAcceptanceFixture(fixture)
{
    private const string Path = "/api/customer-mobile/orders/receipt";
    private Task<HttpResponseMessage> ReceiptAsync(CustomerCheckoutSubmissionDto submission, string? session = null) =>
        SendAsync(Path, new CustomerOrderRecoveryDto
        {
            SchemaVersion = 1,
            OperationId = submission.OperationId,
            PayloadHash = submission.PayloadHash
        }, session);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Original_owner_proof_reads_frozen_receipt_with_separate_current_status_and_no_contact_or_tokens(bool customer)
    {
        if (customer) AuthenticateAsUser();
        var submission = await PreparedAsync(await IntentAsync());
        var accepted = await OrderResolutionAsync(await SubmitAsync(submission));
        await using (var update = DatabaseFixture.CreateContext())
        {
            var order = await update.Orders.SingleAsync();
            order.Status = OrderStatus.Preparing;
            (await update.Products.SingleAsync(product => product.Id == ProductId)).Name = "Changed catalogue name";
            (await update.RestaurantInfo.SingleAsync()).Currency = "EUR";
            await update.SaveChangesAsync();
        }
        await EnableAsync(OrderType.Takeaway, false);
        var response = await ReceiptAsync(submission);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        var raw = await response.Content.ReadAsStringAsync();
        raw.Should().NotContain("customerEmail").And.NotContain("customerName").And.NotContain("customerPhone")
            .And.NotContain("quickActionToken").And.NotContain("guestStatusToken").And.NotContain("payloadHash")
            .And.NotContain("Changed catalogue name").And.NotContain(submission.Intent.Contact.Email);
        using var document = JsonDocument.Parse(raw);
        var receipt = document.RootElement.GetProperty("data");
        receipt.GetProperty("schemaVersion").GetInt32().Should().Be(1);
        receipt.GetProperty("orderId").GetGuid().Should().Be(accepted.OrderId!.Value);
        receipt.GetProperty("status").GetString().Should().Be("Preparing");
        receipt.GetProperty("paymentStatus").GetString().Should().Be("Pending");
        receipt.GetProperty("currency").GetString().Should().Be("CHF");
        receipt.GetProperty("money").GetProperty("total").GetDecimal().Should().Be(17m);
        receipt.GetProperty("totalPaid").GetDecimal().Should().Be(0m);
        receipt.GetProperty("remainingAmount").GetDecimal().Should().Be(17m);
        receipt.GetProperty("items")[0].GetProperty("name").GetString().Should().Be("Native mutation fixture");
        receipt.GetProperty("items")[0].GetProperty("basketItemId").ValueKind.Should().Be(JsonValueKind.Null);
        await using var read = DatabaseFixture.CreateContext();
        (await read.Orders.CountAsync()).Should().Be(1);
        (await read.Set<CustomerOrderOperation>().CountAsync()).Should().Be(1);
        (await read.Baskets.Include(value => value.Items).SingleAsync()).Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Unseen_cancelled_deleted_and_foreign_receipts_never_disclose_an_order()
    {
        var submission = await PreparedAsync(await IntentAsync());
        (await ReceiptAsync(submission)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        await OrderResolutionAsync(await ResolveAsync(submission, cancel: true));
        (await ReceiptAsync(submission)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        submission = await PreparedAsync(await IntentAsync());
        await OrderResolutionAsync(await SubmitAsync(submission));
        (await ReceiptAsync(submission, Guid.NewGuid().ToString("D"))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ReceiptAsync(submission with { PayloadHash = new string('f', 64) })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        await using (var update = DatabaseFixture.CreateContext())
        {
            (await update.Orders.SingleAsync()).IsDeleted = true;
            await update.SaveChangesAsync();
        }
        (await ReceiptAsync(submission)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_retained_operation_cannot_read_an_order_after_its_customer_ownership_changes(bool customer)
    {
        if (customer) AuthenticateAsUser();
        var submission = await PreparedAsync(await IntentAsync());
        await OrderResolutionAsync(await SubmitAsync(submission));
        await using (var update = DatabaseFixture.CreateContext())
        {
            var ownerId = Guid.Parse(RestaurantSystem.IntegrationTests.Common.TestAuthHandler.UserId);
            if (customer)
            {
                var deletion = new DeleteUserCommandHandler(update, Mock.Of<ICurrentUserService>(),
                    new RetainedCustomerDataScrubber(update), NullLogger<DeleteUserCommandHandler>.Instance);
                (await deletion.Handle(new DeleteUserCommand(ownerId, Permanent: true), CancellationToken.None))
                    .Success.Should().BeTrue();
            }
            else
            {
                (await update.Orders.SingleAsync()).UserId = ownerId;
                await update.SaveChangesAsync();
            }
        }
        (await ReceiptAsync(submission)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

}

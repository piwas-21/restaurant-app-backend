using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using System.Collections.Concurrent;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.TestHost;
using RestaurantSystem.Api.Common.TenantFeatures;
using RestaurantSystem.IntegrationTests.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Features.OrderAmendments.Services;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Interfaces;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Features.TableServiceSessions.Dtos;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

[Collection("Database Lane 4")]
public sealed class OrderAmendmentIntegrationTests(DatabaseFixture fixture) : IntegrationTestBase(fixture)
{
    private static readonly System.Text.Json.JsonSerializerOptions KitchenChangeJsonOptions =
        new(System.Text.Json.JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter() }
        };
    private readonly TestOrderNotificationService _notifications = new();
    private Guid _sourceOrderId;
    private Guid _productId;
    private Guid _replacementProductId;
    private Guid _originalItemId;
    private Guid _tableId;

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        services.PostConfigure<TenantFeatureSettings>(settings => settings.OrderAmendmentsV1 = true);
        services.RemoveAll<IOrderDisplayCurrencyResolver>();
        services.RemoveAll<IOrderNotificationService>();
        services.RemoveAll<IOrderAmendmentReservationGuard>();
        services.AddSingleton<IOrderDisplayCurrencyResolver, TestCurrencyResolver>();
        services.AddSingleton<IOrderNotificationService>(_notifications);
        services.AddSingleton<IOrderAmendmentReservationGuard, TestReservationGuard>();
    }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();
        await using var context = DatabaseFixture.CreateContext();
        _productId = await context.Products.Where(product => product.Name == "Test Pizza")
            .Select(product => product.Id).SingleAsync();
        _replacementProductId = await context.Products.Where(product => product.Name == "Test Cola")
            .Select(product => product.Id).SingleAsync();
        await context.Products.Where(product => product.Id == _productId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(product => product.KitchenType, KitchenType.BackKitchen));
        await context.Products.Where(product => product.Id == _replacementProductId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(product => product.KitchenType, KitchenType.FrontKitchen));
        _tableId = Guid.NewGuid();
        context.Tables.Add(new Table
        {
            Id = _tableId,
            TableNumber = "7",
            MaxGuests = 4,
            IsActive = true,
            CreatedBy = nameof(OrderAmendmentIntegrationTests)
        });
        var source = NewSourceOrder();
        _sourceOrderId = source.Id;
        _originalItemId = Guid.NewGuid();
        source.SubTotal = 5m;
        source.Total = 5m;
        source.RemainingAmount = 5m;
        source.Items.Add(new OrderItem
        {
            Id = _originalItemId,
            OrderId = source.Id,
            ProductName = "Earlier soup",
            Quantity = 1,
            UnitPrice = 5m,
            ItemTotal = 5m,
            CreatedBy = nameof(OrderAmendmentIntegrationTests)
        });
        context.Orders.Add(source);
        await context.SaveChangesAsync();
    }

    [Theory]
    [InlineData(OrderAmendmentChangeKind.Void)]
    [InlineData(OrderAmendmentChangeKind.Replace)]
    [InlineData(OrderAmendmentChangeKind.InstructionChange)]
    public async Task Instruction_only_request_checks_committed_source_line_changes(OrderAmendmentChangeKind priorKind)
    {
        await using var context = DatabaseFixture.CreateContext();
        var prior = NewFeedAmendment(_sourceOrderId, _sourceOrderId, Guid.NewGuid(),
            OrderAmendmentState.Committed, DateTime.UtcNow);
        prior.SupplementOrderId = null;
        prior.ChangesJson = OrderAmendmentJson.Serialize(new[]
        {
            new OrderAmendmentChangeSnapshot(_originalItemId, priorKind, 1, 1,
                priorKind == OrderAmendmentChangeKind.InstructionChange,
                new OrderItemDto { Id = _originalItemId, ProductName = "Original soup", Quantity = 1 }, null)
        });
        context.Set<OrderAmendment>().Add(prior);
        await context.SaveChangesAsync();
        var request = new OrderAmendmentQuoteRequest
        {
            Changes = [new OrderAmendmentLineChangeRequest
            {
                OrderItemId = _originalItemId,
                Kind = OrderAmendmentChangeKind.InstructionChange,
                Current = new CreateOrderItemDto { ProductId = _productId, Quantity = 1,
                    SpecialInstructions = "Updated preparation" }
            }]
        };

        Func<Task> validate = () => OrderAmendmentRangeValidator.ValidateAsync(context,
            _sourceOrderId, request, new Dictionary<Guid, int> { [_originalItemId] = 1 }, CancellationToken.None);

        await validate.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task Addition_commit_replays_by_actor_operation_without_duplicating_order_or_note()
    {
        AuthenticateAsAdmin();
        var sourceVersion = await ReadSourceVersionAsync();
        var quote = await QuoteAsync(new OrderAmendmentQuoteRequest
        {
            ExpectedOrderVersion = sourceVersion,
            Additions = [new CreateOrderItemDto { ProductId = _productId, Quantity = 1 }]
        });

        quote.Success.Should().BeTrue();
        quote.Data!.SupplementOrder.Should().NotBeNull();
        quote.Data.SourceOrder.GuestStatusToken.Should().BeNull();
        quote.Data.SourceOrder.CustomerEmail.Should().BeNull();
        quote.Data.SourceOrder.Notes.Should().BeNull();
        quote.Data.SupplementOrder!.GuestStatusToken.Should().BeNull();
        quote.Data.SupplementOrder.CustomerEmail.Should().BeNull();
        quote.Data.SupplementOrder.Notes.Should().BeNull();
        quote.Data.Changes.Should().BeEmpty("new items are dispatched through their supplement order");
        quote.Data.FinancialPreview.AddedAmountMinor.Should().BeGreaterThan(0);
        await using (var quoted = DatabaseFixture.CreateContext())
        {
            (await quoted.Orders.CountAsync()).Should().Be(1, "quote is a read-only preview");
            (await quoted.OrderBillingSnapshots.CountAsync()).Should().Be(0,
                "a quote does not persist the earning evaluation as an accepted snapshot");
            (await quoted.Set<OrderAmendment>().SingleAsync()).State.Should().Be(OrderAmendmentState.Quoted);
            (await quoted.OrderOperationalNotes.CountAsync()).Should().Be(0);
        }
        var request = new OrderAmendmentCommitRequest
        {
            AmendmentId = quote.Data.AmendmentId,
            ClientOperationId = Guid.NewGuid(),
            ExpectedOrderVersion = quote.Data.ExpectedOrderVersion,
            ExpectedAccountRevision = quote.Data.ExpectedAccountRevision,
            ReviewAcknowledged = true
        };

        var first = await CommitAsync(request);
        first.Success.Should().BeTrue();
        await using (var context = DatabaseFixture.CreateContext())
        {
            var supplement = await context.Orders.SingleAsync(order => order.Id == first.Data!.SupplementOrderId);
            supplement.Notes = "A later edit after this committed result.";
            await context.SaveChangesAsync();
        }
        var replay = await CommitAsync(request);
        var mismatchedReplay = await CommitAsync(
            request with { ExpectedOrderVersion = request.ExpectedOrderVersion + 1 },
            HttpStatusCode.Conflict);
        var crossOrderReplay = await CommitAsync(Guid.NewGuid(), request, HttpStatusCode.Conflict);
        var lookup = await LookupOperationAsync(request.ClientOperationId);
        var unknown = await LookupOperationAsync(Guid.NewGuid());
        var history = await ReadHistoryAsync();
        await AssertFeatureDisabledRecoveryAsync(request, first.Data!);

        replay.Success.Should().BeTrue();
        replay.Data.Should().BeEquivalentTo(first.Data);
        lookup.Data!.Status.Should().Be(OrderAmendmentOperationStatus.Committed);
        lookup.Data.Result.Should().BeEquivalentTo(first.Data);
        unknown.Data!.Status.Should().Be(OrderAmendmentOperationStatus.Unknown);
        unknown.Data.Result.Should().BeNull();
        history.Data.Should().ContainSingle().Which.ActorRole.Should().Be("Admin");
        mismatchedReplay.Success.Should().BeFalse("an actor cannot reuse the idempotency key with another payload");
        crossOrderReplay.Success.Should().BeFalse("the route source order is part of commit identity");
        _notifications.CreatedOrderIds.Should().ContainSingle(id => id == first.Data!.SupplementOrderId);
        await using var verify = DatabaseFixture.CreateContext();
        var committedAmendment = await verify.Set<OrderAmendment>().SingleAsync();
        committedAmendment.CommitResultJson.Should().NotBeNullOrWhiteSpace();
        (await verify.Orders.CountAsync()).Should().Be(2, "the retry returns the original supplement");
        var billing = await verify.OrderBillingSnapshots.AsNoTracking()
            .SingleAsync(snapshot => snapshot.OrderId == first.Data!.SupplementOrderId);
        billing.Currency.Should().Be("CHF");
        (await verify.OrderOperationalNotes.CountAsync(note => note.OrderId == _sourceOrderId)).Should().Be(1);
        (await verify.Orders.SingleAsync(order => order.Id == _sourceOrderId))
            .Status.Should().Be(OrderStatus.Confirmed, "amendments preserve source lifecycle state");
        var supplementOrder = await verify.Orders.Include(order => order.Items)
            .SingleAsync(order => order.Id == first.Data!.SupplementOrderId);
        supplementOrder.Items.Should().ContainSingle(item => item.ProductId == _productId)
            .And.NotContain(item => item.Id == _originalItemId || item.ProductName == "Earlier soup",
                "the source order's existing lines must not be reprinted in a supplement");
        replay.Data!.SupplementOrder!.Notes.Should().NotBe("A later edit after this committed result.");
        first.Data!.SupplementOrder!.GuestStatusToken.Should().BeNull();
        first.Data.SupplementOrder.CustomerName.Should().BeNull();
        first.Data.SupplementOrder.CustomerEmail.Should().BeNull();
        first.Data.SupplementOrder.CustomerPhone.Should().BeNull();
        first.Data.SupplementOrder.DeliveryAddress.Should().BeNull();
        first.Data.SupplementOrder.Payments.Should().BeEmpty();
        first.Data.SupplementOrder.StatusHistory.Should().BeEmpty();
        var replaySnapshotJson = committedAmendment.CommitResultJson!;
        replaySnapshotJson.Should().NotContain("private-amendment-customer@example.test");
        replaySnapshotJson.Should().NotContain("Amendment customer");
        replaySnapshotJson.Should().NotContain("guestStatusToken");
    }

    [Fact]
    public async Task Commit_allocates_a_fresh_number_after_an_intervening_order_consumes_the_preview_number()
    {
        AuthenticateAsAdmin();
        var quote = await QuoteAsync(new OrderAmendmentQuoteRequest
        {
            ExpectedOrderVersion = await ReadSourceVersionAsync(),
            Additions = [new CreateOrderItemDto { ProductId = _productId, Quantity = 1 }]
        });
        quote.Data!.SupplementOrder!.OrderNumber.Should().BeEmpty("a preview has no reserved daily number");

        string interveningNumber;
        using (var scope = Factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await using var transaction = await context.Database.BeginTransactionAsync();
            interveningNumber = await scope.ServiceProvider.GetRequiredService<IOrderNumberGenerator>().GenerateAsync();
            var intervening = NewSourceOrder();
            intervening.OrderNumber = interveningNumber;
            context.Orders.Add(intervening);
            await context.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        var result = await CommitAsync(new OrderAmendmentCommitRequest
        {
            AmendmentId = quote.Data.AmendmentId,
            ClientOperationId = Guid.NewGuid(),
            ExpectedOrderVersion = quote.Data.ExpectedOrderVersion,
            ExpectedAccountRevision = quote.Data.ExpectedAccountRevision,
            ReviewAcknowledged = true
        });

        result.Success.Should().BeTrue();
        result.Data!.SupplementOrder!.OrderNumber.Should().NotBeNullOrWhiteSpace()
            .And.NotBe(interveningNumber);
        await using var verify = DatabaseFixture.CreateContext();
        var numbers = await verify.Orders.Select(order => order.OrderNumber).ToListAsync();
        numbers.Should().OnlyHaveUniqueItems().And.HaveCount(3);
    }

    [Fact]
    public async Task Concurrent_same_operation_replay_notifies_order_created_once()
    {
        AuthenticateAsAdmin();
        var quote = await QuoteAsync(new OrderAmendmentQuoteRequest
        {
            ExpectedOrderVersion = await ReadSourceVersionAsync(),
            Additions = [new CreateOrderItemDto { ProductId = _productId, Quantity = 1 }]
        });
        quote.Success.Should().BeTrue();
        var request = new OrderAmendmentCommitRequest
        {
            AmendmentId = quote.Data!.AmendmentId,
            ClientOperationId = Guid.NewGuid(),
            ExpectedOrderVersion = quote.Data.ExpectedOrderVersion,
            ExpectedAccountRevision = quote.Data.ExpectedAccountRevision,
            ReviewAcknowledged = true
        };

        var results = await Task.WhenAll(
            CommitAsync(request),
            CommitAsync(request));

        results.Should().OnlyContain(result => result.Success);
        results[0].Data.Should().BeEquivalentTo(results[1].Data);
        _notifications.CreatedOrderIds.Should()
            .ContainSingle(id => id == results[0].Data!.SupplementOrderId);
        await using var verify = DatabaseFixture.CreateContext();
        (await verify.Orders.CountAsync()).Should().Be(2);
        (await verify.Set<OrderAmendment>().SingleAsync()).State.Should().Be(OrderAmendmentState.Committed);
    }

    [Fact]
    public async Task Replaced_item_keeps_old_to_new_link_and_dispatches_current_on_its_new_station()
    {
        const string replacementInstruction = "  private replacement preparation note  ";
        await RegisterReadyStationsDeviceAsync();
        var originalItemId = await AddReleasedSourceLineAsync();
        await using (var sourceContext = DatabaseFixture.CreateContext())
        {
            await sourceContext.OrderItems.Where(item => item.Id == originalItemId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(
                    item => item.SpecialInstructions, "private previous preparation note"));
        }
        AuthenticateAsAdmin();
        var quote = await QuoteAsync(new OrderAmendmentQuoteRequest
        {
            ExpectedOrderVersion = await ReadSourceVersionAsync(),
            Reason = "Guest chose a different item",
            ReleaseAdditionsToKitchen = true,
            Changes = [new OrderAmendmentLineChangeRequest
            {
                OrderItemId = originalItemId,
                Kind = OrderAmendmentChangeKind.Replace,
                StartOrdinal = 1,
                Quantity = 1,
                Current = new CreateOrderItemDto
                {
                    ProductId = _replacementProductId,
                    Quantity = 1,
                    SpecialInstructions = replacementInstruction
                }
            }]
        });
        quote.Success.Should().BeTrue();
        quote.Data!.SupplementOrder.Should().NotBeNull();
        quote.Data.Changes.Should().ContainSingle().Which.ReplacementDispatchedOrderId
            .Should().Be(quote.Data.SupplementOrder!.Id);
        quote.Data.Changes.Single().Current!.SpecialInstructions.Should().BeNull(
            "quote change snapshots do not return guest-entered free text");
        quote.Data.Changes.Single().Previous.SpecialInstructions.Should().BeNull();

        var commit = await CommitAsync(new OrderAmendmentCommitRequest
        {
            AmendmentId = quote.Data!.AmendmentId,
            ClientOperationId = Guid.NewGuid(),
            ExpectedOrderVersion = quote.Data.ExpectedOrderVersion,
            ExpectedAccountRevision = quote.Data.ExpectedAccountRevision,
            ReviewAcknowledged = true
        });

        commit.Success.Should().BeTrue();
        commit.Data!.SupplementOrder!.Items.Should().ContainSingle()
            .Which.SpecialInstructions.Should().BeNull("commit replay snapshots omit free-text instructions");
        await using var verify = DatabaseFixture.CreateContext();
        var supplement = await verify.Orders.Include(order => order.Items)
            .SingleAsync(order => order.Id == commit.Data!.SupplementOrderId);
        supplement.IsKitchenReleased.Should().BeTrue();
        supplement.Items.Single().SpecialInstructions.Should().Be(replacementInstruction);
        var supplementKitchenTargets = await verify.OrderRoutingStates
            .Where(state => state.OrderId == supplement.Id && state.Target != DevicePrintTarget.Cashier)
            .Select(state => state.Target).ToListAsync();
        supplementKitchenTargets.Should().ContainSingle().Which.Should().Be(DevicePrintTarget.FrontKitchen);

        var correction = await verify.OrderOperationalNotes.SingleAsync(note =>
            note.OrderId == _sourceOrderId && note.AmendmentId == quote.Data.AmendmentId);
        correction.KitchenTarget.Should().Be(DevicePrintTarget.BackKitchen);
        var changes = System.Text.Json.JsonSerializer.Deserialize<List<PrinterFeedChangeDto>>(
            correction.KitchenChangesJson!, KitchenChangeJsonOptions)!;
        changes.Should().ContainSingle();
        changes[0].Kind.Should().Be(KitchenChangeKind.Replace);
        changes[0].Previous!.Id.Should().Be(originalItemId);
        changes[0].Current!.ProductId.Should().Be(_replacementProductId);
        changes[0].Current!.Id.Should().Be(supplement.Items.Single().Id);
        changes[0].ReplacementDispatchedOrderId.Should().Be(supplement.Id);
        changes[0].ReplacementDispatchedOrderNumber.Should().Be(supplement.OrderNumber);
        changes[0].Current!.SpecialInstructions.Should().Be(replacementInstruction,
            "the immutable kitchen correction retains the exact item-specific instruction needed by the station");
        changes[0].Previous!.SpecialInstructions.Should().Be("private previous preparation note");
        var history = await ReadHistoryAsync();
        history.Data.Should().ContainSingle().Which.Changes.Single().Current!.SpecialInstructions.Should().BeNull(
            "history change snapshots do not return guest-entered free text");
        history.Data.Single().Changes.Single().Previous.SpecialInstructions.Should().BeNull();
        (await verify.Set<OrderAmendment>().SingleAsync())
            .CommitResultJson.Should().NotContain("private replacement preparation note");
    }

    [Fact]
    public async Task Held_replacement_cancels_old_kitchen_line_without_dispatched_current()
    {
        await RegisterReadyStationsDeviceAsync();
        var originalItemId = await AddReleasedSourceLineAsync();
        AuthenticateAsAdmin();
        var quote = await QuoteAsync(new OrderAmendmentQuoteRequest
        {
            ExpectedOrderVersion = await ReadSourceVersionAsync(),
            Reason = "Hold the replacement for review",
            ReleaseAdditionsToKitchen = false,
            Changes = [new OrderAmendmentLineChangeRequest
            {
                OrderItemId = originalItemId,
                Kind = OrderAmendmentChangeKind.Replace,
                StartOrdinal = 1,
                Quantity = 1,
                Current = new CreateOrderItemDto { ProductId = _replacementProductId, Quantity = 1 }
            }]
        });
        quote.Success.Should().BeTrue();

        var commit = await CommitAsync(new OrderAmendmentCommitRequest
        {
            AmendmentId = quote.Data!.AmendmentId,
            ClientOperationId = Guid.NewGuid(),
            ExpectedOrderVersion = quote.Data.ExpectedOrderVersion,
            ExpectedAccountRevision = quote.Data.ExpectedAccountRevision,
            ReviewAcknowledged = true
        });

        commit.Success.Should().BeTrue();
        await using var verify = DatabaseFixture.CreateContext();
        var supplement = await verify.Orders.SingleAsync(order => order.Id == commit.Data!.SupplementOrderId);
        supplement.IsKitchenReleased.Should().BeFalse();
        (await verify.OrderRoutingStates.AnyAsync(state => state.OrderId == supplement.Id)).Should().BeFalse();
        var correction = await verify.OrderOperationalNotes.SingleAsync(note => note.AmendmentId == quote.Data.AmendmentId);
        correction.KitchenTarget.Should().Be(DevicePrintTarget.BackKitchen);
        var changes = System.Text.Json.JsonSerializer.Deserialize<List<PrinterFeedChangeDto>>(
            correction.KitchenChangesJson!, KitchenChangeJsonOptions)!;
        changes.Should().ContainSingle();
        changes[0].Kind.Should().Be(KitchenChangeKind.Void);
        changes[0].Previous!.Id.Should().Be(originalItemId);
        changes[0].Current.Should().BeNull();
        changes[0].ReplacementDispatchedOrderId.Should().BeNull();
        changes[0].ReplacementDispatchedOrderNumber.Should().BeNull();
    }

    [Fact]
    public async Task Printer_feed_backlinks_only_committed_supplements_and_reads_soft_deleted_source_metadata()
    {
        var now = DateTime.UtcNow;
        Guid committedSupplementId;
        Guid quotedCandidateId;
        Guid ordinaryOrderId;
        Guid amendmentId;
        string sourceOrderNumber;
        await using (var context = DatabaseFixture.CreateContext())
        {
            var source = await context.Orders.SingleAsync(order => order.Id == _sourceOrderId);
            source.IsDeleted = true;
            sourceOrderNumber = source.OrderNumber;

            var committedSupplement = NewReleasedFeedOrder("AM-COMMIT-SUPP", "supplement-guest-secret");
            var quotedCandidate = NewReleasedFeedOrder("AM-QUOTE-CAND", "quoted-guest-secret");
            var ordinaryOrder = NewReleasedFeedOrder("AM-ORDINARY", "ordinary-guest-secret");
            committedSupplementId = committedSupplement.Id;
            quotedCandidateId = quotedCandidate.Id;
            ordinaryOrderId = ordinaryOrder.Id;
            amendmentId = Guid.NewGuid();
            context.Orders.AddRange(committedSupplement, quotedCandidate, ordinaryOrder);
            context.Set<OrderAmendment>().AddRange(
                NewFeedAmendment(source.Id, committedSupplement.Id, amendmentId, OrderAmendmentState.Committed, now),
                NewFeedAmendment(source.Id, quotedCandidate.Id, Guid.NewGuid(), OrderAmendmentState.Quoted, now));
            await context.SaveChangesAsync();
        }

        AuthenticateAsDevice();
        using var response = await Client.GetAsync("/api/orders/printer-feed");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var feed = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        feed["success"]!.GetValue<bool>().Should().BeTrue();
        var items = feed["data"]!["items"]!.AsArray();
        var committed = items.Single(item => item!["id"]!.GetValue<Guid>() == committedSupplementId)!;
        var printContext = committed["amendmentPrintContext"]!;
        printContext["amendmentId"]!.GetValue<Guid>().Should().Be(amendmentId);
        printContext["sourceOrderId"]!.GetValue<Guid>().Should().Be(_sourceOrderId);
        printContext["sourceOrderNumber"]!.GetValue<string>().Should().Be(sourceOrderNumber);
        committed.ToJsonString().Should().NotContain("supplement-guest-secret");

        var uncommitted = items.Single(item => item!["id"]!.GetValue<Guid>() == quotedCandidateId)!;
        uncommitted.AsObject().ContainsKey("amendmentPrintContext").Should().BeFalse();
        var ordinary = items.Single(item => item!["id"]!.GetValue<Guid>() == ordinaryOrderId)!;
        ordinary.AsObject().ContainsKey("amendmentPrintContext").Should().BeFalse();
        items.Should().NotContain(item => item!["id"]!.GetValue<Guid>() == _sourceOrderId,
            "soft-deleted source orders stay out of the normal ticket feed");
    }

    [Fact]
    public async Task Preparing_paid_line_quote_allows_server_acknowledgement_and_retains_gateway_refund_custody()
    {
        var itemId = await PreparePaidOrderAsync();
        var sourceVersion = await ReadSourceVersionAsync();
        var quoteRequest = new OrderAmendmentQuoteRequest
        {
            ExpectedOrderVersion = sourceVersion,
            Reason = "Guest changed the order after release",
            Changes = [new OrderAmendmentLineChangeRequest
            {
                OrderItemId = itemId,
                Kind = OrderAmendmentChangeKind.Void,
                StartOrdinal = 1,
                Quantity = 1
            }]
        };

        AuthenticateAsRole(UserRole.Server);
        var refused = await QuoteAsync(quoteRequest, HttpStatusCode.BadRequest);
        refused.Success.Should().BeFalse("preparing corrections require an explicit override acknowledgement");
        var accepted = await QuoteAsync(quoteRequest with { PreparingOverrideAcknowledged = true });

        accepted.Success.Should().BeTrue();
        accepted.Data!.FinancialPreview.RefundState.Should()
            .Be(OrderAmendmentRefundState.GatewayRefundRequired);
        accepted.Data.FinancialPreview.CreditState.Should()
            .Be(OrderAmendmentCreditState.PendingAllocationReview);
        accepted.Data.FinancialPreview.ResolutionStatus.Should()
            .Be(OrderAmendmentFinancialResolutionStatus.Pending);
        await using var verify = DatabaseFixture.CreateContext();
        (await verify.Orders.SingleAsync(order => order.Id == _sourceOrderId))
            .Status.Should().Be(OrderStatus.Preparing);
        (await verify.OrderPayments.SingleAsync(payment => payment.OrderId == _sourceOrderId))
            .RefundedAmount.Should().BeNull("quoting does not claim a provider-side refund happened");
    }

    [Fact]
    public async Task Served_line_correction_remains_cashier_or_admin_only()
    {
        var itemId = await PreparePaidOrderAsync();
        await using (var context = DatabaseFixture.CreateContext())
        {
            await context.Orders.Where(value => value.Id == _sourceOrderId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(value => value.Status, OrderStatus.Delivered)
                    .SetProperty(value => value.Version, value => value.Version + 1));
        }

        var request = new OrderAmendmentQuoteRequest
        {
            ExpectedOrderVersion = await ReadSourceVersionAsync(),
            Reason = "Guest reported a missing item",
            PreparingOverrideAcknowledged = true,
            Changes = [new OrderAmendmentLineChangeRequest
            {
                OrderItemId = itemId,
                Kind = OrderAmendmentChangeKind.Void,
                StartOrdinal = 1,
                Quantity = 1
            }]
        };
        AuthenticateAsRole(UserRole.Server);

        var refused = await QuoteAsync(request, HttpStatusCode.Forbidden);

        refused.Success.Should().BeFalse();
    }

    [Fact]
    public async Task Commit_rejects_quote_after_a_new_round_advances_the_account_revision()
    {
        AuthenticateAsRole(UserRole.Server);
        var session = await OpenSessionAsync();
        await using (var context = DatabaseFixture.CreateContext())
        {
            var source = await context.Orders.SingleAsync(order => order.Id == _sourceOrderId);
            source.Type = OrderType.DineIn;
            source.TableId = _tableId;
            source.TableNumber = 7;
            source.ServiceSessionId = session.ServiceSessionId;
            await context.SaveChangesAsync();
        }

        var quote = await QuoteAsync(new OrderAmendmentQuoteRequest
        {
            ExpectedOrderVersion = await ReadSourceVersionAsync(),
            ExpectedAccountRevision = session.AccountRevision,
            Additions = [new CreateOrderItemDto { ProductId = _productId, Quantity = 1 }]
        });
        quote.Success.Should().BeTrue();

        using var newRound = await PostJsonAsync("/api/staff/orders/round", new
        {
            clientOperationId = Guid.NewGuid(),
            releaseToKitchen = false,
            type = nameof(OrderType.DineIn),
            tableId = _tableId,
            serviceSessionId = session.ServiceSessionId,
            paymentState = "Unpaid",
            items = new[] { new { productId = _productId, quantity = 1 } }
        });
        newRound.StatusCode.Should().Be(HttpStatusCode.OK);

        var refused = await CommitAsync(new OrderAmendmentCommitRequest
        {
            AmendmentId = quote.Data!.AmendmentId,
            ClientOperationId = Guid.NewGuid(),
            ExpectedOrderVersion = quote.Data.ExpectedOrderVersion,
            ExpectedAccountRevision = quote.Data.ExpectedAccountRevision,
            ReviewAcknowledged = true
        }, HttpStatusCode.Conflict);

        refused.Success.Should().BeFalse("a new round invalidates the reviewed account snapshot");
        await using var verify = DatabaseFixture.CreateContext();
        (await verify.Set<OrderAmendment>().SingleAsync()).State.Should().Be(OrderAmendmentState.Quoted);
        (await verify.Orders.CountAsync(order => order.ServiceSessionId == session.ServiceSessionId)).Should().Be(2);
    }

    [Fact]
    public async Task Marketplace_local_supplement_requires_explicit_consent_and_never_copies_provider_identity()
    {
        await AddProviderReferenceAsync();
        AuthenticateAsRole(UserRole.Cashier);
        var sourceVersion = await ReadSourceVersionAsync();
        var request = new OrderAmendmentQuoteRequest
        {
            ExpectedOrderVersion = sourceVersion,
            Additions = [new CreateOrderItemDto { ProductId = _productId, Quantity = 1 }]
        };

        var refused = await QuoteAsync(request, HttpStatusCode.BadRequest);
        refused.Success.Should().BeFalse();
        var accepted = await QuoteAsync(request with
        {
            LocalProviderSupplementConsent = true,
            ProviderConsentNote = "Staff will reconcile the provider order separately."
        });

        accepted.Success.Should().BeTrue();
        accepted.Data!.ProviderProcedure.Should().Contain("No provider edit is sent");
        var commit = await CommitAsync(new OrderAmendmentCommitRequest
        {
            AmendmentId = accepted.Data.AmendmentId,
            ClientOperationId = Guid.NewGuid(),
            ExpectedOrderVersion = accepted.Data.ExpectedOrderVersion,
            ReviewAcknowledged = true
        });

        commit.Success.Should().BeTrue();
        await using var verify = DatabaseFixture.CreateContext();
        var amendment = await verify.Set<OrderAmendment>().SingleAsync();
        amendment.RequestJson.Should().Contain("Staff will reconcile the provider order separately.");
        var supplement = await verify.Orders.Include(order => order.ExternalReference)
            .Include(order => order.Items)
            .SingleAsync(order => order.Id == commit.Data!.SupplementOrderId);
        supplement.ExternalReference.Should().BeNull();
        supplement.Items.Should().ContainSingle();
        (await verify.ExternalOrderReferences.SingleAsync(reference => reference.OrderId == _sourceOrderId))
            .ExternalOrderId.Should().Be("provider-order-original");
    }

    private async Task<int> ReadSourceVersionAsync()
    {
        await using var context = DatabaseFixture.CreateContext();
        return await context.Orders.Where(order => order.Id == _sourceOrderId)
            .Select(order => order.Version).SingleAsync();
    }

    private async Task<TableServiceSessionDto> OpenSessionAsync()
    {
        using var response = await PostJsonAsync("/api/table-service-sessions", new { tableId = _tableId });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await ReadResponseAsync<ApiResponse<TableServiceSessionDto>>(response))!.Data!;
    }

    private async Task<Guid> AddReleasedSourceLineAsync()
    {
        await using (var context = DatabaseFixture.CreateContext())
        {
            await context.Orders.Where(order => order.Id == _sourceOrderId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(order => order.IsKitchenReleased, true)
                    .SetProperty(order => order.Version, order => order.Version + 1)
                    .SetProperty(order => order.SubTotal, 17.99m)
                    .SetProperty(order => order.Total, 17.99m)
                    .SetProperty(order => order.RemainingAmount, 17.99m));
        }

        var itemId = Guid.NewGuid();
        await using (var context = DatabaseFixture.CreateContext())
        {
            context.OrderItems.Add(new OrderItem
            {
                Id = itemId,
                OrderId = _sourceOrderId,
                ProductId = _productId,
                ProductName = "Old back-kitchen pizza",
                Quantity = 1,
                UnitPrice = 12.99m,
                ItemTotal = 12.99m,
                CreatedBy = nameof(OrderAmendmentIntegrationTests)
            });
            await context.SaveChangesAsync();
        }

        return itemId;
    }

    private async Task RegisterReadyStationsDeviceAsync()
    {
        var deviceId = $"amendment-{Guid.NewGuid():N}";
        AuthenticateAsDevice();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/devices/heartbeat")
        {
            Content = System.Net.Http.Json.JsonContent.Create(new
            {
                feedRunning = true,
                lastSuccessfulPollAt = DateTime.UtcNow,
                kitchenRoutingMode = "Stations",
                targetCapabilities = new[]
                {
                    new { target = "Cashier", isSupported = true, isConfigured = true,
                        autoPrintEnabled = true, printerName = "cashier" },
                    new { target = "FrontKitchen", isSupported = true, isConfigured = true,
                        autoPrintEnabled = true, printerName = "front" },
                    new { target = "BackKitchen", isSupported = true, isConfigured = true,
                        autoPrintEnabled = true, printerName = "back" }
                }
            })
        };
        request.Headers.Add("X-Device-Id", deviceId);
        (await Client.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<ApiResponse<OrderAmendmentQuoteDto>> QuoteAsync(
        OrderAmendmentQuoteRequest request, HttpStatusCode expectedStatus = HttpStatusCode.OK)
    {
        using var response = await PostJsonAsync(
            $"/api/staff/orders/{_sourceOrderId}/amendments/quote", request);
        response.StatusCode.Should().Be(expectedStatus);
        return (await ReadResponseAsync<ApiResponse<OrderAmendmentQuoteDto>>(response))!;
    }

    private async Task<ApiResponse<OrderAmendmentCommitDto>> CommitAsync(
        OrderAmendmentCommitRequest request, HttpStatusCode expectedStatus = HttpStatusCode.OK)
        => await CommitAsync(_sourceOrderId, request, expectedStatus);

    private async Task<ApiResponse<OrderAmendmentCommitDto>> CommitAsync(
        Guid orderId,
        OrderAmendmentCommitRequest request,
        HttpStatusCode expectedStatus = HttpStatusCode.OK)
    {
        using var response = await PostJsonAsync(
            $"/api/staff/orders/{orderId}/amendments/commit", request);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(expectedStatus, body);
        return System.Text.Json.JsonSerializer.Deserialize<ApiResponse<OrderAmendmentCommitDto>>(
            body, JsonOptions)!;
    }

    private async Task AssertFeatureDisabledRecoveryAsync(
        OrderAmendmentCommitRequest request, OrderAmendmentCommitDto committed)
    {
        using var disabledFactory = Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ITenantFeatures>();
            services.AddSingleton<ITenantFeatures>(new TenantFeatures(Options.Create(new TenantFeatureSettings())));
        }));
        using var disabledClient = disabledFactory.CreateClient();
        disabledClient.DefaultRequestHeaders.Add("X-Test-Admin", "true");
        var lookupPath = $"/api/staff/amendment-operations/{request.ClientOperationId}";
        using var recovered = await disabledClient.GetAsync(lookupPath);
        recovered.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await ReadResponseAsync<ApiResponse<OrderAmendmentOperationLookupDto>>(recovered);
        result!.Data!.Status.Should().Be(OrderAmendmentOperationStatus.Committed);
        result.Data.Result.Should().BeEquivalentTo(committed);
        using var blocked = await disabledClient.PostAsJsonAsync(
            $"/api/staff/orders/{_sourceOrderId}/amendments/commit", request);
        blocked.StatusCode.Should().Be(HttpStatusCode.NotFound, "disabling amendments still prevents writes");
        disabledClient.DefaultRequestHeaders.Remove("X-Test-Admin");
        disabledClient.DefaultRequestHeaders.Add(TestAuthHandler.RoleHeader, "Cashier");
        using var otherActor = await disabledClient.GetAsync(lookupPath);
        otherActor.StatusCode.Should().Be(HttpStatusCode.OK);
        var otherResult = await ReadResponseAsync<ApiResponse<OrderAmendmentOperationLookupDto>>(otherActor);
        otherResult!.Data!.Status.Should().Be(OrderAmendmentOperationStatus.Unknown);
        otherResult.Data.Result.Should().BeNull("operation IDs do not grant access to another actor's committed result");
    }

    private async Task<ApiResponse<OrderAmendmentOperationLookupDto>> LookupOperationAsync(Guid operationId)
    {
        using var response = await Client.GetAsync($"/api/staff/amendment-operations/{operationId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await ReadResponseAsync<ApiResponse<OrderAmendmentOperationLookupDto>>(response))!;
    }

    private async Task<ApiResponse<List<OrderAmendmentHistoryDto>>> ReadHistoryAsync()
    {
        using var response = await Client.GetAsync($"/api/staff/orders/{_sourceOrderId}/amendments");
        response.EnsureSuccessStatusCode();
        return (await ReadResponseAsync<ApiResponse<List<OrderAmendmentHistoryDto>>>(response))!;
    }

    private async Task<HttpResponseMessage> PostJsonAsync<T>(string url, T request)
    {
        var body = System.Text.Json.JsonSerializer.Serialize(request, JsonOptions);
        return await Client.PostAsync(url, new StringContent(body, Encoding.UTF8, "application/json"));
    }

    private async Task<Guid> PreparePaidOrderAsync()
    {
        var itemId = Guid.NewGuid();
        await using (var context = DatabaseFixture.CreateContext())
        {
            await context.Orders.Where(value => value.Id == _sourceOrderId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(value => value.Status, OrderStatus.Preparing)
                    .SetProperty(value => value.IsKitchenReleased, true)
                    .SetProperty(value => value.PaymentStatus, PaymentStatus.Completed)
                    .SetProperty(value => value.SubTotal, 15m)
                    .SetProperty(value => value.Total, 15m)
                    .SetProperty(value => value.TotalPaid, 15m)
                    .SetProperty(value => value.RemainingAmount, 0m)
                    .SetProperty(value => value.Version, value => value.Version + 1));
        }

        await using (var context = DatabaseFixture.CreateContext())
        {
            context.OrderItems.Add(new OrderItem
            {
                Id = itemId,
                OrderId = _sourceOrderId,
                ProductName = "Paid burger",
                Quantity = 1,
                UnitPrice = 10m,
                ItemTotal = 10m,
                CreatedBy = nameof(OrderAmendmentIntegrationTests)
            });
            context.OrderPayments.Add(new OrderPayment
            {
                OrderId = _sourceOrderId,
                Amount = 15m,
                Status = PaymentStatus.Completed,
                PaymentMethod = PaymentMethod.OnlinePayment,
                PaymentGateway = "Stripe",
                Currency = "CHF",
                PaymentDate = DateTime.UtcNow,
                CreatedBy = nameof(OrderAmendmentIntegrationTests)
            });
            await context.SaveChangesAsync();
        }

        return itemId;
    }

    private async Task AddProviderReferenceAsync()
    {
        await using var context = DatabaseFixture.CreateContext();
        context.ExternalOrderReferences.Add(new ExternalOrderReference
        {
            OrderId = _sourceOrderId,
            Provider = "Marketplace",
            ExternalStoreId = "provider-store",
            ExternalOrderId = "provider-order-original",
            ExternalDisplayId = "display-123",
            ExternalState = "Accepted",
            LastEventAt = DateTime.UtcNow,
            Currency = "CHF",
            MerchantTotal = 12m,
            PayloadHash = new string('b', 64),
            FulfillmentType = "Takeaway",
            CreatedBy = nameof(OrderAmendmentIntegrationTests)
        });
        await context.SaveChangesAsync();
    }

    private static Order NewReleasedFeedOrder(string orderNumber, string guestStatusToken)
    {
        var now = DateTime.UtcNow;
        return new Order
        {
            Id = Guid.NewGuid(),
            OrderNumber = orderNumber,
            Type = OrderType.Takeaway,
            Status = OrderStatus.Confirmed,
            PaymentStatus = PaymentStatus.Pending,
            IsKitchenReleased = true,
            GuestStatusToken = guestStatusToken,
            OrderDate = now,
            CreatedAt = now,
            CreatedBy = nameof(OrderAmendmentIntegrationTests),
            SubTotal = 10m,
            Total = 10m,
            RemainingAmount = 10m,
            Items = [new OrderItem
            {
                Id = Guid.NewGuid(),
                ProductName = orderNumber,
                Quantity = 1,
                UnitPrice = 10m,
                ItemTotal = 10m,
                CreatedBy = nameof(OrderAmendmentIntegrationTests)
            }]
        };
    }

    private OrderAmendment NewFeedAmendment(
        Guid sourceOrderId, Guid supplementOrderId, Guid amendmentId,
        OrderAmendmentState state, DateTime createdAt) => new()
        {
            Id = amendmentId,
            SourceOrderId = sourceOrderId,
            SupplementOrderId = supplementOrderId,
            ActorUserId = Guid.NewGuid(),
            ActorRole = "Admin",
            State = state,
            PayloadHash = new string('a', 64),
            ExpectedOrderVersion = 1,
            ExpiresAt = createdAt.AddMinutes(5),
            RequestJson = "{}",
            ChangesJson = "[]",
            SourceSnapshotJson = "{}",
            FinancialResolutionJson = "{}",
            CommittedAt = state == OrderAmendmentState.Committed ? createdAt : null,
            CommitPayloadHash = state == OrderAmendmentState.Committed ? new string('b', 64) : null,
            CreatedAt = createdAt,
            CreatedBy = nameof(OrderAmendmentIntegrationTests)
        };

    private Order NewSourceOrder() => new()
    {
        Id = Guid.NewGuid(),
        OrderNumber = $"AM-{Guid.NewGuid():N}"[..16],
        Type = OrderType.Takeaway,
        Status = OrderStatus.Confirmed,
        PaymentStatus = PaymentStatus.Pending,
        IsKitchenReleased = false,
        Version = 1,
        Total = 0m,
        RemainingAmount = 0m,
        OrderDate = DateTime.UtcNow,
        GuestStatusToken = "source-guest-status-secret",
        CustomerName = "Amendment customer",
        CustomerEmail = "private-amendment-customer@example.test",
        CustomerPhone = "+41000000000",
        Notes = "private source note",
        CreatedBy = nameof(OrderAmendmentIntegrationTests)
    };

    private sealed class TestOrderNotificationService : IOrderNotificationService
    {
        public ConcurrentQueue<Guid> CreatedOrderIds { get; } = new();

        public Task SendOrderConfirmedAsync(Order order, int estimatedPreparationMinutes,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SendOrderDelayedAsync(Order order, int delayMinutes,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SendNewOrderMailAsync(Order order, OrderDto orderDto,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SendOrderConfirmationAsync(OrderDto order) => Task.CompletedTask;

        public Task NotifyOrderCreatedAsync(OrderDto order)
        {
            CreatedOrderIds.Enqueue(order.Id);
            return Task.CompletedTask;
        }

        public Task NotifyFocusOrderUpdateAsync(OrderDto order) => Task.CompletedTask;
    }

    private sealed class TestReservationGuard : IOrderAmendmentReservationGuard
    {
        public Task AssertUnitsMutableAsync(
            Guid orderId,
            IReadOnlyList<OrderAmendmentUnitScope> scopes,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class TestCurrencyResolver : IOrderDisplayCurrencyResolver
    {
        public string? Resolve(Order order) => order.ExternalReference?.Currency ?? "CHF";
    }
}

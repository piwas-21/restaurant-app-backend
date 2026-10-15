using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Features.Orders.Commands.UpdateOrderStatusCommand;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

public sealed class OrderPreparationServiceTests
{
    private static readonly OrderWorkflowSettings Workflow = new()
    {
        ConfirmedPreparationMinutes = 20
    };

    [Fact]
    public async Task Known_zero_product_snapshot_is_not_replaced_by_workflow_fallback()
    {
        var order = OrderWithItems(new OrderItem
        {
            CreatedBy = nameof(OrderPreparationServiceTests),
            PreparationTimeMinutesSnapshot = 0
        });
        using var context = Context();
        MarkItemsLoaded(context, order);
        var service = new OrderPreparationService(context, Options.Create(Workflow));

        var minutes = await service.ResolveEffectiveMinutesAsync(order, null, CancellationToken.None);

        minutes.Should().Be(0);
    }

    [Fact]
    public async Task Workflow_fallback_is_used_only_when_no_product_snapshot_is_known()
    {
        var order = OrderWithItems(new OrderItem { CreatedBy = nameof(OrderPreparationServiceTests) });
        using var context = Context();
        MarkItemsLoaded(context, order);
        var service = new OrderPreparationService(context, Options.Create(Workflow));

        var minutes = await service.ResolveEffectiveMinutesAsync(order, null, CancellationToken.None);

        minutes.Should().Be(20);
    }

    [Fact]
    public async Task Multiple_product_and_component_snapshots_use_the_longest_preparation_time()
    {
        var order = OrderWithItems(
            new OrderItem { CreatedBy = nameof(OrderPreparationServiceTests), PreparationTimeMinutesSnapshot = 8 },
            new OrderItem { CreatedBy = nameof(OrderPreparationServiceTests), PreparationTimeMinutesSnapshot = 17 },
            new OrderItem { CreatedBy = nameof(OrderPreparationServiceTests), PreparationTimeMinutesSnapshot = 12 });
        using var context = Context();
        MarkItemsLoaded(context, order);
        var service = new OrderPreparationService(context, Options.Create(Workflow));

        var minutes = await service.ResolveEffectiveMinutesAsync(order, null, CancellationToken.None);

        minutes.Should().Be(17);
    }

    [Fact]
    public async Task Authorized_zero_override_wins_and_acceptance_starts_the_expected_ready_time()
    {
        var order = OrderWithItems(new OrderItem
        {
            CreatedBy = nameof(OrderPreparationServiceTests),
            PreparationTimeMinutesSnapshot = 30
        });
        using var context = Context();
        MarkItemsLoaded(context, order);
        var service = new OrderPreparationService(context, Options.Create(Workflow));
        var acceptedAt = new DateTime(2026, 10, 15, 10, 30, 0, DateTimeKind.Utc);

        var minutes = await service.ApplyAcceptedAsync(order, 0, acceptedAt, CancellationToken.None);

        minutes.Should().Be(0);
        order.EffectivePreparationMinutes.Should().Be(0);
        order.ExpectedReadyAt.Should().Be(acceptedAt);
    }

    [Fact]
    public async Task Pending_approval_has_preparation_duration_but_no_running_ready_timestamp()
    {
        var order = OrderWithItems(new OrderItem
        {
            CreatedBy = nameof(OrderPreparationServiceTests),
            PreparationTimeMinutesSnapshot = 13
        });
        using var context = Context();
        MarkItemsLoaded(context, order);
        var service = new OrderPreparationService(context, Options.Create(Workflow));

        var minutes = await service.ApplyPendingApprovalAsync(order, null, CancellationToken.None);

        minutes.Should().Be(13);
        order.EffectivePreparationMinutes.Should().Be(13);
        order.ExpectedReadyAt.Should().BeNull();
    }

    [Fact]
    public async Task Accepting_a_pending_approval_keeps_its_saved_duration_and_starts_countdown_then()
    {
        var order = OrderWithItems(new OrderItem
        {
            CreatedBy = nameof(OrderPreparationServiceTests),
            PreparationTimeMinutesSnapshot = 15
        });
        order.EffectivePreparationMinutes = 45;
        using var context = Context();
        MarkItemsLoaded(context, order);
        var service = new OrderPreparationService(context, Options.Create(Workflow));
        var acceptedAt = new DateTime(2026, 10, 15, 10, 30, 0, DateTimeKind.Utc);

        var minutes = await service.ApplyAcceptedAsync(order, null, acceptedAt, CancellationToken.None);

        minutes.Should().Be(45);
        order.ExpectedReadyAt.Should().Be(acceptedAt.AddMinutes(45));
    }

    [Fact]
    public void Status_validator_allows_an_explicit_zero_minute_override()
    {
        var command = new UpdateOrderStatusCommand
        {
            OrderId = Guid.NewGuid(),
            NewStatus = RestaurantSystem.Domain.Common.Enums.OrderStatus.Confirmed,
            EstimatedPreparationMinutes = 0
        };

        new UpdateOrderStatusCommandValidator().Validate(command).IsValid.Should().BeTrue();
    }

    private static ApplicationDbContext Context() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=unused_order_prep;Username=test;Password=test")
            .Options);

    private static Order OrderWithItems(params OrderItem[] items)
    {
        return new Order
        {
            Id = Guid.NewGuid(),
            CreatedBy = nameof(OrderPreparationServiceTests),
            Items = items.ToList()
        };
    }

    private static void MarkItemsLoaded(ApplicationDbContext context, Order order) =>
        context.Entry(order).Collection(candidate => candidate.Items).IsLoaded = true;
}

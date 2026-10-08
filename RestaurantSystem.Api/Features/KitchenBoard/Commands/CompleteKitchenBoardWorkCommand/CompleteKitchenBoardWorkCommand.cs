using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Common.TenantFeatures;
using RestaurantSystem.Api.Features.KitchenBoard.Dtos;
using RestaurantSystem.Api.Features.KitchenBoard.Services;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.KitchenBoard.Commands.CompleteKitchenBoardWorkCommand;

public sealed record CompleteKitchenBoardWorkCommand(
    Guid OrderId,
    Guid WorkItemId,
    string Kind,
    int ExpectedOrderVersion,
    long? ExpectedAccountRevision) : ICommand<ApiResponse<KitchenBoardWorkCompletionDto>>;

public sealed class CompleteKitchenBoardWorkCommandHandler(
    ApplicationDbContext context,
    ICurrentUserService currentUser,
    TimeProvider timeProvider,
    ITenantFeatures features)
    : ICommandHandler<CompleteKitchenBoardWorkCommand, ApiResponse<KitchenBoardWorkCompletionDto>>
{
    public async Task<ApiResponse<KitchenBoardWorkCompletionDto>> Handle(
        CompleteKitchenBoardWorkCommand command, CancellationToken cancellationToken)
    {
        KitchenBoardFeaturePolicy.RequireEnabled(features);
        if (!Enum.TryParse<KitchenBoardWorkKind>(command.Kind, true, out var kind)
            || !Enum.IsDefined(kind) || command.ExpectedOrderVersion < 1
            || command.ExpectedAccountRevision is <= 0)
        {
            throw new BadRequestException("Kitchen work completion input is invalid.");
        }

        await using var scope = await OrderAccountMutationScope.BeginAsync(
            context, command.OrderId, cancellationToken);
        var order = await context.Orders.Include(value => value.RoutingStates)
            .SingleOrDefaultAsync(value => value.Id == command.OrderId && !value.IsDeleted,
                cancellationToken)
            ?? throw new NotFoundException("Order not found.");

        var existing = await context.KitchenBoardWorkCompletions.AsNoTracking()
            .SingleOrDefaultAsync(value => value.OrderId == command.OrderId
                && value.WorkItemId == command.WorkItemId && value.Kind == kind, cancellationToken);
        if (existing is not null)
        {
            await ValidateReplayIdentityAsync(command, kind, existing);
            await scope.CommitAsync(cancellationToken);
            return ApiResponse<KitchenBoardWorkCompletionDto>.SuccessWithData(ToDto(existing));
        }

        var revision = await ValidateWorkAsync(command, kind, order, cancellationToken);

        if (order.Version != command.ExpectedOrderVersion)
        {
            throw new ConflictException(
                "The order changed. Refresh the kitchen work before completing it.",
                ErrorCodes.OrderVersionConflict);
        }

        await RequireNotConfiguredRoute(order, kind, command.WorkItemId, cancellationToken);
        var completedAt = timeProvider.GetUtcNow().UtcDateTime;
        var completion = new KitchenBoardWorkCompletion
        {
            OrderId = order.Id,
            WorkItemId = command.WorkItemId,
            Kind = kind,
            AcknowledgedOrderVersion = order.Version,
            AccountRevision = revision,
            CreatedAt = completedAt,
            CreatedBy = currentUser.GetAuditIdentifier(),
        };
        context.KitchenBoardWorkCompletions.Add(completion);
        await context.SaveChangesAsync(cancellationToken);
        if (kind == KitchenBoardWorkKind.AmendmentCorrection)
        {
            var target = await context.OrderOperationalNotes.AsNoTracking()
                .Where(value => value.Id == command.WorkItemId && value.OrderId == order.Id)
                .Select(value => value.KitchenTarget)
                .SingleAsync(cancellationToken);
            if (target.HasValue)
            {
                await KitchenBoardSequenceWriter.TouchCorrectionAsync(
                    context, order.Id, command.WorkItemId, target.Value, cancellationToken);
            }
        }

        await scope.CommitAsync(cancellationToken);
        return ApiResponse<KitchenBoardWorkCompletionDto>.SuccessWithData(ToDto(completion));
    }

    private static Task ValidateReplayIdentityAsync(
        CompleteKitchenBoardWorkCommand command,
        KitchenBoardWorkKind kind,
        KitchenBoardWorkCompletion existing)
    {
        if (kind == KitchenBoardWorkKind.InitialOrder)
        {
            if (command.WorkItemId != command.OrderId || command.ExpectedAccountRevision.HasValue
                || existing.AccountRevision.HasValue)
            {
                throw new ConflictException("The completed kitchen work does not match this request.");
            }

            return Task.CompletedTask;
        }

        if (!command.ExpectedAccountRevision.HasValue
            || existing.AccountRevision != command.ExpectedAccountRevision)
        {
            throw new ConflictException("The completed kitchen correction does not match this revision.");
        }

        return Task.CompletedTask;
    }

    private async Task<long?> ValidateWorkAsync(
        CompleteKitchenBoardWorkCommand command,
        KitchenBoardWorkKind kind,
        Order order,
        CancellationToken cancellationToken)
    {
        if (!order.IsKitchenReleased)
            throw new ConflictException("Kitchen work is not released for this order.");

        if (order.ExternalReference is not null)
            throw new ConflictException("Marketplace preparation must use its delivery-channel workflow.");

        if (kind == KitchenBoardWorkKind.InitialOrder)
        {
            if (command.WorkItemId != order.Id || command.ExpectedAccountRevision.HasValue
                || order.Status != OrderStatus.Ready)
            {
                throw new ConflictException("Initial kitchen work can be completed only when the order is ready.");
            }

            return null;
        }

        if (command.WorkItemId == order.Id || !command.ExpectedAccountRevision.HasValue)
            throw new BadRequestException("An amendment correction requires its exact revision.");

        var note = await context.OrderOperationalNotes.AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == command.WorkItemId
                && value.OrderId == order.Id && value.Audience == OrderNoteAudience.Kitchen
                && value.KitchenChangesJson != null, cancellationToken)
            ?? throw new NotFoundException("Kitchen correction not found.");
        if (note.WithdrawnAt.HasValue)
            throw new ConflictException("The kitchen correction was withdrawn and needs no completion.");
        if (note.AccountRevision != command.ExpectedAccountRevision)
            throw new ConflictException("The kitchen correction revision changed. Refresh the work feed.");
        return note.AccountRevision;
    }

    private async Task RequireNotConfiguredRoute(
        Order order,
        KitchenBoardWorkKind kind,
        Guid workItemId,
        CancellationToken cancellationToken)
    {
        if (kind == KitchenBoardWorkKind.InitialOrder)
        {
            if (!KitchenBoardWorkRules.HasOnlyNotConfiguredRoutes(order))
                throw RoutingUnavailable();
            return;
        }

        var note = await context.OrderOperationalNotes.AsNoTracking()
            .SingleAsync(value => value.Id == workItemId && value.OrderId == order.Id,
                cancellationToken);
        var route = note.KitchenTarget.HasValue
            ? order.RoutingStates.FirstOrDefault(value => value.IsRequired
                && value.Target == note.KitchenTarget.Value)
            : null;
        if (route?.Status != DevicePrintStatus.NotConfigured || route.DeviceId is not null)
            throw RoutingUnavailable();
    }

    private static ConflictException RoutingUnavailable() => new(
        "The required printer route is not confirmed as unconfigured; resolve its printer state first.",
        ErrorCodes.RequiredRoutingUnresolved);

    private static KitchenBoardWorkCompletionDto ToDto(KitchenBoardWorkCompletion work) => new(
        work.OrderId,
        work.WorkItemId,
        work.Kind.ToString(),
        work.AccountRevision,
        work.AcknowledgedOrderVersion,
        work.Sequence,
        work.CreatedAt,
        true);
}

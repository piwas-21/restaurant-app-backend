using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Basket.Interfaces;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Features.TableGuestVisits.Dtos;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.TableGuestVisits.Services.CustomerRounds;

public sealed class CustomerTableRoundService(ApplicationDbContext context, IBasketWriteCoordinator coordinator,
    ICustomerTableRoundAccess access, ICustomerTableRoundProofs proofs, ICustomerTableRoundReceipts receipts,
    ICustomerTableRoundExecution execution, ICustomerTableRoundDelivery delivery) : ICustomerTableRoundService
{
    public async Task<CustomerTableRoundPreparedDto> PrepareAsync(string sessionId, string? participantToken,
        CustomerTableRoundIntentDto request, CancellationToken cancellationToken)
    {
        await ValidateAsync(request, cancellationToken);
        if (request.OwnerProof is not null || request.PayloadHash is not null)
            throw new BadRequestException("Review a new round without an existing proof.");
        var owner = access.Capture(sessionId, request.SchemaVersion, request.TenantId);
        return await UnderLockAsync(sessionId, owner, request.OperationId, () =>
            execution.PrepareAsync(sessionId, owner, participantToken, request, cancellationToken), cancellationToken);
    }
    public Task<CustomerTableRoundResolutionDto> SubmitAsync(string sessionId, string? participantToken,
        CustomerTableRoundIntentDto request, CancellationToken cancellationToken) => RunAsync(sessionId,
            new(request.SchemaVersion, request.OperationId, request.TenantId, request.PayloadHash ?? string.Empty,
                request.OwnerProof ?? string.Empty), request, participantToken, false, cancellationToken);
    public Task<CustomerTableRoundResolutionDto> ResolveAsync(string sessionId, CustomerTableRoundProofDto proof,
        bool cancelAttempt, CancellationToken cancellationToken) => RunAsync(sessionId, proof, null, null, cancelAttempt, cancellationToken);

    private async Task<CustomerTableRoundResolutionDto> RunAsync(string sessionId, CustomerTableRoundProofDto proof,
        CustomerTableRoundIntentDto? request, string? token, bool cancelAttempt, CancellationToken cancellationToken)
    {
        var owner = access.Capture(sessionId, proof.SchemaVersion, proof.TenantId);
        var binding = proofs.Require(proof, owner);
        if (request is not null)
        {
            await ValidateAsync(request, cancellationToken);
            proofs.RequireInput(request, owner, binding);
        }
        var decision = await UnderLockAsync(sessionId, owner, proof.OperationId, () =>
            DecideAsync(owner, proof, binding, request, token, cancelAttempt, cancellationToken), cancellationToken);
        if (decision.Order is { } order) await delivery.NotifyAsync(order, proof.OperationId, cancellationToken);
        return decision.Resolution;
    }
    private async Task<CustomerTableRoundDecision> DecideAsync(CustomerTableRoundOwner owner, CustomerTableRoundProofDto proof,
        CustomerTableRoundBinding binding, CustomerTableRoundIntentDto? request, string? token, bool cancelAttempt,
        CancellationToken cancellationToken)
    {
        // Capture has already required the exact canonical session spelling.
        var sessionId = owner.BasketSessionId.ToString("D");
        var previous = await receipts.ReadAsync(owner, proof, binding, cancellationToken);
        if (previous is not null)
        {
            await access.LockCustomerAsync(owner, cancellationToken);
            RequireOwner(sessionId, proof.SchemaVersion, proof.TenantId, owner);
            return new(previous, null);
        }
        if (request is not null)
            return await execution.SubmitAsync(sessionId, owner, proof, binding, request, token, cancellationToken);
        await access.LockCustomerAsync(owner, cancellationToken);
        RequireOwner(sessionId, proof.SchemaVersion, proof.TenantId, owner);
        if (!cancelAttempt)
            return new(new(1, proof.OperationId, binding.ServiceSessionId, CustomerOrderOperationOutcome.Unseen, null), null);
        var stopped = await receipts.SaveAsync(owner, proof, binding, null, cancellationToken);
        await access.LockCustomerAsync(owner, cancellationToken);
        RequireOwner(sessionId, proof.SchemaVersion, proof.TenantId, owner);
        if (await receipts.ReadAsync(owner, proof, binding, cancellationToken) != stopped) throw Unavailable();
        return new(stopped, null);
    }
    private async Task<T> UnderLockAsync<T>(string sessionId, CustomerTableRoundOwner owner, Guid operationId,
        Func<Task<T>> work, CancellationToken cancellationToken)
    {
        if (context.Database.CurrentTransaction is not null || context.ChangeTracker.HasChanges())
            throw new InvalidOperationException("Native table round operations require an isolated clean context.");
        try
        {
            return await coordinator.RunAsync(sessionId, owner.CustomerId, async () =>
            {
                await receipts.LockAsync(operationId, cancellationToken);
                return await work();
            }, cancellationToken);
        }
        catch { context.ChangeTracker.Clear(); throw; }
    }
    private void RequireOwner(string sessionId, int schemaVersion, Guid tenantId, CustomerTableRoundOwner owner) =>
        access.RequireOriginal(sessionId, schemaVersion, tenantId, owner);
    private static async Task ValidateAsync(CustomerTableRoundIntentDto request, CancellationToken cancellationToken)
    {
        if (!(await new CustomerTableRoundIntentDtoValidator().ValidateAsync(request, cancellationToken)).IsValid)
            throw new BadRequestException("Review the current table account and basket before adding a round.");
    }
    private static NotFoundException Unavailable() => new("Customer table round operation unavailable.");
}

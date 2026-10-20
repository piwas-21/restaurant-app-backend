using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Basket.Services.Operations;
using RestaurantSystem.Api.Features.Reservations.Dtos;
using RestaurantSystem.Api.Features.User.Services;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Reservations.Services;

public sealed class CustomerReservationOperationGuard(ApplicationDbContext context, ICurrentUserService currentUser,
    IHttpContextAccessor httpContext, IOptions<CustomerMobileSettings> settings, CustomMediator mediator, ITenantClock clock, ICustomerReservationProofService proofs)
    : ICustomerReservationOperationGuard
{
    public CustomerReservationOperationOwner Capture(string sessionId, CustomerReservationCreateDto request)
    {
        var owner = InputOwner(sessionId, request);
        if (!string.Equals(owner.PayloadHash, request.PayloadHash, StringComparison.Ordinal))
            throw new ConflictException("The prepared reservation details changed. Review the original attempt.");
        return owner;
    }

    public CustomerReservationOperationOwner CaptureProof(string sessionId, CustomerReservationProofDto proof)
    {
        var owner = CaptureOwner(sessionId, proof.SchemaVersion, proof.OperationId, proof.TenantId);
        if (proof.PayloadHash is null || !Regex.IsMatch(proof.PayloadHash, "^[a-f0-9]{64}$", RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1)))
            throw new BadRequestException("A complete original reservation proof is required.");
        owner = owner with { PayloadHash = proof.PayloadHash };
        proofs.Require(proof, owner);
        return owner;
    }

    public async Task<CustomerReservationProofDto> PrepareAsync(string sessionId, CustomerReservationCreateDto request,
        CancellationToken cancellationToken)
    {
        var owner = InputOwner(sessionId, request);
        await RequireAcceptingAsync(request, owner, cancellationToken);
        if (owner.CustomerId is { } customer && !await CustomerAccountAccess.ForOwner(context, customer)
                .AsNoTracking().AnyAsync(cancellationToken))
            throw new NotFoundException("Customer account unavailable.");
        await RequireAcceptingAsync(request, owner, cancellationToken);
        // Preparation neither reserves a slot nor creates a receipt. Hash binds original input AND owner/session.
        return new(1, request.OperationId, request.TenantId, owner.PayloadHash,
            proofs.Create(request.TenantId, request.OperationId, owner));
    }

    private CustomerReservationOperationOwner InputOwner(string sessionId, CustomerReservationCreateDto request)
    {
        var owner = CaptureOwner(sessionId, request.SchemaVersion, request.OperationId, request.TenantId);
        return owner with
        {
            PayloadHash = CustomerBasketOperationHash.Payload(new
            {
                Purpose = "customer-reservation-create-v1",
                owner.OwnerHash,
                Request = request with { PayloadHash = null, OwnerProof = null }
            })
        };
    }

    private CustomerReservationOperationOwner CaptureOwner(string sessionId, int? version, Guid operationId, Guid tenant)
    {
        CustomerBasketAccess.EnsureCustomerOrGuest(currentUser, httpContext.HttpContext);
        var session = CustomerBasketOperationHash.Session(sessionId);
        Guid? customer = currentUser.IsAuthenticated
            ? currentUser.UserId ?? throw new UnauthorizedAccessException("Renew customer authentication before booking.")
            : null;
        if (version != 1 || operationId == Guid.Empty)
            throw new BadRequestException("A supported booking schema and operation ID are required.");
        if (tenant == Guid.Empty || tenant != settings.Value.TenantId)
            throw new NotFoundException("Customer reservation operation unavailable.");
        return new(customer, CustomerBasketOperationHash.Owner(session, customer), string.Empty);
    }

    public Task LockOwnerAsync(CustomerReservationOperationOwner owner, CancellationToken cancellationToken) =>
        owner.CustomerId is { } customer
            ? CustomerAccountWriteGuard.AcquireAsync(context, customer, true, cancellationToken)
            : Task.CompletedTask;

    public async Task RequireAcceptingAsync(CustomerReservationCreateDto request, CustomerReservationOperationOwner owner,
        CancellationToken cancellationToken)
    {
        var bootstrap = await CustomerReservationReadAccess.RequireAvailableAsync(mediator, cancellationToken);
        if (request.TenantId != bootstrap.TenantId || request.TimeZone != bootstrap.Timezone
            || owner.CustomerId.HasValue && bootstrap.Capabilities.GetValueOrDefault("accounts") != "enabled")
            throw new NotFoundException("Customer reservations are unavailable. Refresh this restaurant before booking.");
    }

    public void EnsureFutureSlot(CustomerReservationCreateDto request)
    {
        var now = clock.Now;
        var today = DateOnly.FromDateTime(now.DateTime);
        if (request.Date is not { } date || request.StartTime is not { } start
            || date < today || date == today && start <= now.TimeOfDay)
            throw new ConflictException("This reservation start is no longer in the future. Check current times.");
    }
}

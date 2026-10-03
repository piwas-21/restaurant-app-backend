using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Common.TenantFeatures;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

internal sealed class OrderAmendmentQuoteService : IOrderAmendmentQuoteService
{
    private static readonly TimeSpan QuoteLifetime = TimeSpan.FromMinutes(5);
    private readonly ApplicationDbContext _context;
    private readonly ITenantFeatures _features;
    private readonly ICurrentUserService _currentUser;
    private readonly OrderAmendmentSupplementBuilder _supplements;
    private readonly OrderAmendmentChangeBuilder _changes;
    private readonly IOrderMappingService _mapping;
    private readonly IOrderDisplayCurrencyResolver _currencyResolver;
    private readonly IOrderAmendmentFinancialResolution _financial;

    public OrderAmendmentQuoteService(
        ApplicationDbContext context,
        ITenantFeatures features,
        ICurrentUserService currentUser,
        OrderAmendmentSupplementBuilder supplements,
        OrderAmendmentChangeBuilder changes,
        IOrderMappingService mapping,
        IOrderDisplayCurrencyResolver currencyResolver,
        IOrderAmendmentFinancialResolution financial)
    {
        _context = context;
        _features = features;
        _currentUser = currentUser;
        _supplements = supplements;
        _changes = changes;
        _mapping = mapping;
        _currencyResolver = currencyResolver;
        _financial = financial;
    }

    public async Task<OrderAmendmentQuoteDto> QuoteAsync(
        Guid orderId, OrderAmendmentQuoteRequest request, CancellationToken cancellationToken)
    {
        OrderAmendmentPolicy.RequireFeature(_features);
        var actorId = OrderAmendmentPolicy.RequireActor(_currentUser);
        var normalized = Normalize(request);
        var now = DateTime.UtcNow;
        OrderAmendment amendment;
        OrderAmendmentQuoteDto response;

        await using (var transaction = await _context.Database.BeginTransactionAsync(cancellationToken))
        {
            try
            {
                var source = await OrderAmendmentOrderLoader.LoadSourceAsync(_context, orderId, cancellationToken)
                    ?? throw new NotFoundException("The source order was not found.");
                await OrderAmendmentFinancialGuard.AssertNoPendingSourceResolutionAsync(
                    _context, source.Id, cancellationToken);
                var refundAuthority = await OrderAmendmentRefundAuthorityReader.ReadAsync(
                    _context, source, _currencyResolver, cancellationToken);
                OrderAmendmentPolicy.ValidateOrderContext(source, normalized, refundAuthority);
                OrderAmendmentPolicy.ValidateChangeAuthority(source, normalized, _currentUser);
                var sourceDto = await _mapping.MapToOrderDtoAsync(source, cancellationToken);
                var sourceLines = source.Items.Where(item => !item.ParentOrderItemId.HasValue).ToList();
                var sourceQuantities = sourceLines.ToDictionary(item => item.Id, item => item.Quantity);
                await OrderAmendmentRangeValidator.ValidateAsync(
                    _context, source.Id, normalized, sourceQuantities, cancellationToken);

                var supplement = await _supplements.BuildAsync(source, normalized, cancellationToken);
                // A rolled-back preview cannot reserve a human-facing daily order number.
                if (supplement is not null)
                    supplement.OrderNumber = string.Empty;
                var supplementDto = supplement is null ? null : OrderAmendmentSnapshots.MapBuiltOrder(_mapping, supplement);
                var changes = await _changes.BuildAsync(
                    source, sourceDto, normalized, supplementDto, cancellationToken);
                var financial = await _financial.PreviewAsync(source, changes, supplement, cancellationToken);

                var amendmentId = Guid.NewGuid();
                var expiresAt = now.Add(QuoteLifetime);
                amendment = new OrderAmendment
                {
                    Id = amendmentId,
                    SourceOrderId = source.Id,
                    ServiceSessionId = source.ServiceSessionId,
                    ActorUserId = actorId,
                    ActorRole = _currentUser.Role?.ToString() ?? string.Empty,
                    State = OrderAmendmentState.Quoted,
                    PayloadHash = OrderAmendmentJson.Hash(OrderAmendmentJson.Serialize(normalized)),
                    ExpectedOrderVersion = source.Version,
                    ExpectedAccountRevision = normalized.ExpectedAccountRevision,
                    ExpiresAt = expiresAt,
                    RequestJson = OrderAmendmentJson.Serialize(normalized),
                    ChangesJson = OrderAmendmentJson.Serialize(changes),
                    SourceSnapshotJson = OrderAmendmentJson.Serialize(OrderAmendmentSnapshots.Source(sourceDto)),
                    SupplementSnapshotJson = supplementDto is null
                        ? null
                        : OrderAmendmentJson.Serialize(OrderAmendmentSnapshots.Supplement(supplementDto)),
                    FinancialResolutionJson = OrderAmendmentJson.Serialize(financial),
                    CreatedAt = now,
                    CreatedBy = _currentUser.GetAuditIdentifier()
                };
                response = new OrderAmendmentQuoteDto
                {
                    AmendmentId = amendmentId,
                    SourceOrderId = source.Id,
                    ServiceSessionId = source.ServiceSessionId,
                    ExpectedOrderVersion = source.Version,
                    ExpectedAccountRevision = normalized.ExpectedAccountRevision,
                    ExpiresAt = expiresAt,
                    SourceOrder = sourceDto,
                    SupplementOrder = supplementDto,
                    Changes = changes,
                    FinancialPreview = financial,
                    ProviderProcedure = source.ExternalReference is null
                        ? null
                        : "No provider edit is sent. Staff must reconcile the provider order separately."
                };
                await transaction.RollbackAsync(cancellationToken);
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                _context.ChangeTracker.Clear();
                throw;
            }
        }

        _context.ChangeTracker.Clear();
        await using var persistence = await _context.Database.BeginTransactionAsync(cancellationToken);
        _context.Set<OrderAmendment>().Add(amendment);
        await _context.SaveChangesAsync(cancellationToken);
        await persistence.CommitAsync(cancellationToken);
        return OrderAmendmentResponseRedactor.RedactQuote(response);
    }

    private static OrderAmendmentQuoteRequest Normalize(OrderAmendmentQuoteRequest request) => request with
    {
        Reason = NormalizeOptionalText(request.Reason, 500),
        ProviderConsentNote = NormalizeOptionalText(request.ProviderConsentNote, 500),
        Additions = request.Additions ?? [],
        Changes = request.Changes ?? []
    };

    private static string? NormalizeOptionalText(string? value, int limit)
    {
        var sanitized = OrderAmendmentPolicy.SanitizeText(value, limit);
        return sanitized.Length == 0 ? null : sanitized;
    }
}

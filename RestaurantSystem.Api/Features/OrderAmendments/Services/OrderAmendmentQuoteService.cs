using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Common.TenantFeatures;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

internal sealed class OrderAmendmentQuoteService : IOrderAmendmentQuoteService
{
    private readonly ApplicationDbContext _context;
    private readonly ITenantFeatures _features;
    private readonly ICurrentUserService _currentUser;
    private readonly IOrderAmendmentQuotePreviewBuilder _previewBuilder;
    private readonly IOrderDisplayCurrencyResolver _currencyResolver;
    private readonly IOrderAmendmentFinancialResolution _financial;
    private readonly OrderAmendmentResolutionSettings _resolutionSettings;

    public OrderAmendmentQuoteService(
        ApplicationDbContext context,
        ITenantFeatures features,
        ICurrentUserService currentUser,
        IOrderAmendmentQuotePreviewBuilder previewBuilder,
        IOrderDisplayCurrencyResolver currencyResolver,
        IOrderAmendmentFinancialResolution financial,
        IOptions<OrderAmendmentResolutionSettings> resolutionSettings)
    {
        _context = context;
        _features = features;
        _currentUser = currentUser;
        _previewBuilder = previewBuilder;
        _currencyResolver = currencyResolver;
        _financial = financial;
        _resolutionSettings = resolutionSettings.Value;
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
                var sourceDto = await _previewBuilder.MapSourceAsync(source, cancellationToken);
                var sourceLines = source.Items.Where(item => !item.ParentOrderItemId.HasValue).ToList();
                var sourceQuantities = sourceLines.ToDictionary(item => item.Id, item => item.Quantity);
                await OrderAmendmentRangeValidator.ValidateAsync(
                    _context, source.Id, normalized, sourceQuantities, cancellationToken);

                var preview = await _previewBuilder.BuildAsync(source, sourceDto, normalized, cancellationToken);
                var supplement = preview.Supplement;
                var supplementDto = preview.SupplementDto;
                var changes = preview.Changes;
                var financial = await _financial.PreviewAsync(source, changes, supplement, cancellationToken);

                var amendmentId = Guid.NewGuid();
                var expiresAt = now.AddMinutes(_resolutionSettings.AmendmentQuoteLifetimeMinutes);
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
        await using var persistence = await OrderAccountMutationScope.BeginAsync(
            _context, orderId, cancellationToken);
        var current = await _context.Orders.AsNoTracking()
            .Where(order => order.Id == orderId && !order.IsDeleted)
            .Select(order => new
            {
                order.Version,
                order.ServiceSessionId,
                AccountRevision = order.ServiceSession == null ? (long?)null : order.ServiceSession.AccountRevision
            }).SingleOrDefaultAsync(cancellationToken);
        if (current is null || current.Version != amendment.ExpectedOrderVersion
            || current.ServiceSessionId != amendment.ServiceSessionId
            || current.AccountRevision != amendment.ExpectedAccountRevision)
            throw new ConflictException("The source order changed while preparing the quote. Refresh and quote again.");
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

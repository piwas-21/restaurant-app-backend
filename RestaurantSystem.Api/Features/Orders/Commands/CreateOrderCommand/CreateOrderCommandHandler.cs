using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Common.TenantFeatures;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Interfaces;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Features.TableGuestVisits.Services;
using RestaurantSystem.Api.Features.TableServiceSessions.Services;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Commands.CreateOrderCommand;

public class CreateOrderCommandHandler : ICommandHandler<CreateOrderCommand, ApiResponse<OrderDto>>
{
    private readonly IOrderCreationWriter _writer;
    private readonly IOrderPostCreationService _postCreation;
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<CreateOrderCommandHandler> _logger;
    private readonly IOrderRoutingService _routing;
    private readonly IOrderFactory _orderFactory;
    private readonly IPreferredLanguageCapture _languages;
    private readonly ITableGuestRoundOperationStore? _guestRounds;
    private readonly ITenantFeatures? _features;

    public CreateOrderCommandHandler(
        IOrderCreationWriter writer,
        IOrderPostCreationService postCreation,
        ApplicationDbContext context,
        ICurrentUserService currentUserService,
        IOrderRoutingService routing,
        IOrderFactory orderFactory,
        IPreferredLanguageCapture languages,
        ILogger<CreateOrderCommandHandler> logger,
        ITableGuestRoundOperationStore? guestRounds = null,
        ITenantFeatures? features = null)
    {
        _writer = writer;
        _postCreation = postCreation;
        _context = context;
        _currentUserService = currentUserService;
        _orderFactory = orderFactory;
        _languages = languages;
        _routing = routing;
        _logger = logger;
        _guestRounds = guestRounds;
        _features = features;
    }

    public async Task<ApiResponse<OrderDto>> Handle(CreateOrderCommand command, CancellationToken cancellationToken)
    {
        var guestContext = command.GuestRoundContext;
        var validationFailure = GuestRoundOrderPolicy.ValidateSubmission(command, _features?.TableVisitReadinessV1 == true);
        if (validationFailure is not null) return validationFailure;

        var ownerId = guestContext is null ? command.UserId ?? _currentUserService.UserId : null;
        var language = await _languages.ForUserAsync(ownerId, cancellationToken);

        using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            TableServiceSession? guestSession = null;
            TableGuestParticipant? guestParticipant = null;
            if (guestContext is not null)
            {
                var guestRounds = _guestRounds ?? throw new InvalidOperationException(
                    "Guest round operations are not registered.");
                var preparation = await guestRounds.PrepareUnderLockAsync(guestContext, cancellationToken);
                if (preparation.Replay is not null)
                {
                    await transaction.CommitAsync(cancellationToken);
                    return preparation.Replay;
                }

                guestSession = preparation.Session;
                guestParticipant = preparation.Participant;
                command.TableNumber = guestSession.TableNumber;
            }

            var acceptedCurrency = await OrderNativeAcceptedCurrency.ResolveForAcceptanceAsync(
                _context, guestSession?.Currency, cancellationToken);

            var draft = await _orderFactory.CreateAsync(command, ownerId, language, cancellationToken);

            if (draft.IsFailed) return ApiResponse<OrderDto>.Failure(draft.Error);

            var order = draft.Order;
            if (guestSession is not null)
            {
                await GuestRoundOrderPolicy.AttachVisitAsync(
                    _context, order, guestSession, cancellationToken);
            }

            async Task RecordGuestRoundAsync()
            {
                if (guestSession is not null && guestParticipant is not null)
                {
                    await _routing.EnsureRoutesAsync(order, cancellationToken);
                    await GuestRoundOrderPolicy.RecordAcceptedRoundAsync(
                        _context, _guestRounds ?? throw new InvalidOperationException(
                            "Guest round operations are not registered."),
                        guestContext!, guestSession, guestParticipant, order, cancellationToken);
                }
            }

            var error = await _writer.WriteAsync(command, draft, acceptedCurrency,
                RecordGuestRoundAsync, cancellationToken);
            if (error is not null) return ApiResponse<OrderDto>.Failure(error);

            await transaction.CommitAsync(cancellationToken);

            return await _postCreation.CompleteAsync(order, draft.PaysOnline, cancellationToken);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(ex, "Error creating order");
            throw;
        }
    }
}

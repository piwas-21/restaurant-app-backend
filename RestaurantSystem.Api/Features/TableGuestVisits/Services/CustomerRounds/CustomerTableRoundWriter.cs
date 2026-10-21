using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Orders.Commands.CreateOrderCommand;
using RestaurantSystem.Api.Features.Orders.Interfaces;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Features.Orders.Services.CustomerCheckout;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.TableGuestVisits.Services.CustomerRounds;

public sealed class CustomerTableRoundWriter(ApplicationDbContext context, IOrderFactory orders, IOrderCreationWriter writer,
    IOrderRoutingService routing, ITableGuestRoundOperationStore operations,
    IPreferredLanguageCapture languages) : ICustomerTableRoundWriter
{
    public async Task<Order> CreateAsync(CustomerTableRoundReview review, TableGuestRoundContext round,
        TableGuestRoundPreparation preparation, CancellationToken cancellationToken)
    {
        var participant = preparation.Participant ?? throw new NotFoundException("Table participant unavailable.");
        var command = new CreateOrderCommand
        {
            Type = OrderType.DineIn,
            ItemsAreServerPriced = true,
            Items = review.Prepared.Items.ToList(),
            Payments = [],
            GuestRoundContext = round
        };
        GuestRoundOrderPolicy.Validate(command);
        command.TableNumber = preparation.Session.TableNumber;
        var language = await languages.ForUserAsync(null, cancellationToken);
        var draft = await orders.CreateAsync(command, null, language, cancellationToken);
        if (draft.IsFailed) throw new BadRequestException(draft.Error);
        var order = draft.Order;
        await GuestRoundOrderPolicy.AttachVisitAsync(context, order, preparation.Session, cancellationToken);
        async Task RecordRoundAsync()
        {
            await routing.EnsureRoutesAsync(order, cancellationToken);
            await GuestRoundOrderPolicy.RecordAcceptedRoundAsync(context, operations, round,
                preparation.Session, participant, order, cancellationToken);
        }
        var error = await writer.WriteAsync(command, draft, review.Context.Currency, RecordRoundAsync, cancellationToken);
        if (error is not null) throw new BadRequestException(error);
        CustomerCheckoutMoneyGuard.Ensure(order);
        var actual = CustomerCheckoutProjection.Create(review.Context, review.Input, order);
        if (actual.QuoteFingerprint != review.Quote.QuoteFingerprint)
            throw new ConflictException("The round price changed. Review its current price before sending.");
        return order;
    }
}

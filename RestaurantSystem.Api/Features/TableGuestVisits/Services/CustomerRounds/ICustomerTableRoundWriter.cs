using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.TableGuestVisits.Services.CustomerRounds;

public interface ICustomerTableRoundWriter
{
    Task<Order> CreateAsync(CustomerTableRoundReview review, TableGuestRoundContext round,
        TableGuestRoundPreparation preparation, CancellationToken cancellationToken);
}

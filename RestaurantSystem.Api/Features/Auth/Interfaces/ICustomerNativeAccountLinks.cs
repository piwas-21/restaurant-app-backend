using RestaurantSystem.Api.Features.Auth.Dtos;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Auth.Interfaces;

public interface ICustomerNativeAccountLinks
{
    Task RequireEnabledAsync(int schemaVersion, CancellationToken cancellationToken);
    string Create(ApplicationUser user, string token, string action);
    CustomerNativeAccountProof Read(string proof, string action);
    Task<string> RequireCurrentEmailAsync(CustomerNativeAccountProof proof, CancellationToken cancellationToken);
}

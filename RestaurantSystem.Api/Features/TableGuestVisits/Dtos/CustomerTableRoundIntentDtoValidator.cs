using FluentValidation;
using RestaurantSystem.Api.Features.TableGuestVisits.Services.CustomerRounds;

namespace RestaurantSystem.Api.Features.TableGuestVisits.Dtos;

public sealed class CustomerTableRoundIntentDtoValidator : AbstractValidator<CustomerTableRoundIntentDto>
{
    public CustomerTableRoundIntentDtoValidator()
    {
        RuleFor(value => value.SchemaVersion).Equal(1);
        RuleFor(value => value.OperationId).NotEmpty();
        RuleFor(value => value.TenantId).NotEmpty();
        RuleFor(value => value.ServiceSessionId).NotEmpty();
        RuleFor(value => value.ExpectedBasketId).NotEmpty();
        RuleFor(value => value.ExpectedAccountRevision).InclusiveBetween(1, CustomerTableRoundHashes.MaximumSafeRevision);
        RuleFor(value => value.ExpectedPurchaseFingerprint).Must(CustomerTableRoundHashes.IsDigest);
    }
}

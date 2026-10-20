using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Addresses.Dtos;
namespace RestaurantSystem.Api.Features.Addresses.Services;

public static class CustomerAddressCommands
{
    public static T Copy<T>(CustomerAddressInput input, T command) where T : AddressContent
    {
        command.Label = input.Label;
        command.AddressLine1 = input.AddressLine1;
        command.AddressLine2 = input.AddressLine2;
        command.City = input.City;
        command.State = input.State;
        command.PostalCode = input.PostalCode;
        command.Country = input.Country;
        command.Phone = input.Phone;
        command.IsDefault = input.IsDefault;
        command.DeliveryInstructions = input.DeliveryInstructions;
        return command;
    }
    public static ApiResponse<CustomerAddressWriteResultDto> Written(ApiResponse<AddressDto> result)
    {
        if (!result.Success || result.Data is not { } address) throw new NotFoundException("Address unavailable.");
        return ApiResponse<CustomerAddressWriteResultDto>.SuccessWithData(new(1,
            new(address.Id, address.Label, address.AddressLine1, address.AddressLine2, address.City,
                address.State, address.PostalCode, address.Country, address.Phone, address.IsDefault, address.DeliveryInstructions)));
    }
    public static ApiResponse<CustomerAddressActionResultDto> Applied(ApiResponse<string> result, Guid id, string state)
    {
        if (!result.Success) throw new NotFoundException("Address unavailable.");
        return ApiResponse<CustomerAddressActionResultDto>.SuccessWithData(new(1, id, state));
    }
}

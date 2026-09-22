using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace RestaurantSystem.Api.Features.Orders.Models;

public readonly record struct OptionalDeviceHeader(bool IsPresent, string? Value);

public sealed class OptionalDeviceHeaderModelBinder : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        var isPresent = bindingContext.HttpContext.Request.Headers.TryGetValue(
            bindingContext.ModelName, out var value);
        if (!isPresent)
        {
            bindingContext.Result = ModelBindingResult.Success(new OptionalDeviceHeader(false, null));
            return Task.CompletedTask;
        }

        bindingContext.ModelState.SetModelValue(
            bindingContext.ModelName,
            new ValueProviderResult(value));
        bindingContext.Result = ModelBindingResult.Success(
            new OptionalDeviceHeader(true, value.ToString()));
        return Task.CompletedTask;
    }
}

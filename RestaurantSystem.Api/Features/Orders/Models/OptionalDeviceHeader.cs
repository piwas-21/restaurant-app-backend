using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace RestaurantSystem.Api.Features.Orders.Models;

public sealed record OptionalDeviceHeader
{
    public required bool IsPresent { get; init; }

    public string? Value { get; init; }
}

public sealed class OptionalDeviceHeaderModelBinder : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        var isPresent = bindingContext.HttpContext.Request.Headers.TryGetValue(
            bindingContext.ModelName, out var value);
        if (!isPresent)
        {
            bindingContext.Result = ModelBindingResult.Success(new OptionalDeviceHeader
            {
                IsPresent = false,
            });
            return Task.CompletedTask;
        }

        bindingContext.ModelState.SetModelValue(
            bindingContext.ModelName,
            new ValueProviderResult(value));
        bindingContext.Result = ModelBindingResult.Success(new OptionalDeviceHeader
        {
            IsPresent = true,
            Value = value.ToString(),
        });
        return Task.CompletedTask;
    }
}

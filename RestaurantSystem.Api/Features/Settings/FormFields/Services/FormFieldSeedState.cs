using RestaurantSystem.Api.Features.Settings.FormFields.Interfaces;

namespace RestaurantSystem.Api.Features.Settings.FormFields.Services;

public sealed class FormFieldSeedState : IFormFieldSeedState
{
    private int _seeded;

    public bool IsSeeded => Volatile.Read(ref _seeded) == 1;

    public void MarkSeeded() => Volatile.Write(ref _seeded, 1);
}

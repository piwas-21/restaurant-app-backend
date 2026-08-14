using System.Collections.Concurrent;
using System.Globalization;
using System.Resources;

namespace RestaurantSystem.Api.Common.Templates;

/// <summary>
/// Explicit-culture lookup over the email resource sets in <c>Resources/Email/*.resx</c>
/// (EMAIL-LOCALISATION-PLAN §3).
/// <para>
/// Deliberately NOT <c>IStringLocalizer</c>: that resolves from the ambient
/// <see cref="CultureInfo.CurrentUICulture"/>, which is unset on exactly the paths that
/// send most mail — the detached order tasks, the Stripe settlement webhook and every
/// BackgroundService. Those would silently render English while tests pass. Here the
/// culture is a value that must be passed, so a caller that forgets it does not compile.
/// </para>
/// <para>
/// A key that is missing from the mail's own set falls back to the shared
/// <c>Common</c> set; a culture with no satellite assembly falls back to the neutral
/// (English) resources, never to an empty string.
/// </para>
/// </summary>
public sealed class EmailText
{
    /// <summary>Resource set holding the strings shared by every mail (footer, sign-off, …).</summary>
    public const string CommonSet = "Common";

    private const string BaseNamePrefix = "RestaurantSystem.Api.Resources.Email.";

    private static readonly ConcurrentDictionary<string, ResourceManager> Managers = new(StringComparer.Ordinal);

    private readonly string _set;
    private readonly CultureInfo _culture;

    private EmailText(string set, CultureInfo culture)
    {
        _set = set;
        _culture = culture;
    }

    /// <summary>Binds a resource set to one culture. Both are required — see the type remarks.</summary>
    public static EmailText For(CultureInfo culture, string set)
    {
        ArgumentNullException.ThrowIfNull(culture);
        ArgumentException.ThrowIfNullOrEmpty(set);

        return new EmailText(set, culture);
    }

    /// <summary>The string for <paramref name="key"/>, rendered in this instance's culture.</summary>
    public string this[string key] => Lookup(key);

    /// <summary>
    /// <see cref="string.Format(IFormatProvider, string, object?[])"/> over the resource string.
    /// The provider is invariant on purpose: every argument is pre-formatted to a string by the
    /// caller, so the culture selects wording only and can never reformat an amount or a date.
    /// </summary>
    public string Format(string key, params object?[] arguments) =>
        string.Format(CultureInfo.InvariantCulture, Lookup(key), arguments);

    private string Lookup(string key)
    {
        // ResourceManager already walks culture -> parent culture -> neutral resources.
        var value = Resources(_set).GetString(key, _culture);

        if (value is null && !string.Equals(_set, CommonSet, StringComparison.Ordinal))
        {
            value = Resources(CommonSet).GetString(key, _culture);
        }

        return value ?? throw new InvalidOperationException(
            $"Email resource '{_set}.{key}' is missing from Resources/Email.");
    }

    private static ResourceManager Resources(string set) =>
        Managers.GetOrAdd(set, static key => new ResourceManager(BaseNamePrefix + key, typeof(EmailText).Assembly));
}

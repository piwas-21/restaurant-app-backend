namespace RestaurantSystem.Api.Features.TranslationWorkbench.Dtos;

/// <summary>A reviewed template value supplied by the trusted tenant import path.</summary>
public sealed record TemplateTranslationEvidence(string FieldKey, string Locale, string Text);

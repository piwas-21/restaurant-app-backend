using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Settings.FormFields.Dtos;
using RestaurantSystem.Api.Features.Settings.FormFields.Interfaces;

namespace RestaurantSystem.Api.Features.Settings.FormFields.Queries.GetFormFieldConfigurationsQuery;

/// <summary>
/// Every customer form field with its configured visibility/requiredness and its
/// registry-driven locked flag, grouped per form. Anonymous read — customer-facing
/// forms need this before render (precedent: OrderTypeConfiguration/enabled).
/// </summary>
public record GetFormFieldConfigurationsQuery() : IQuery<ApiResponse<List<FormFieldsDto>>>;

public class GetFormFieldConfigurationsQueryHandler
    : IQueryHandler<GetFormFieldConfigurationsQuery, ApiResponse<List<FormFieldsDto>>>
{
    private readonly IFormFieldConfigurationService _configurationService;

    public GetFormFieldConfigurationsQueryHandler(IFormFieldConfigurationService configurationService)
        => _configurationService = configurationService;

    public async Task<ApiResponse<List<FormFieldsDto>>> Handle(
        GetFormFieldConfigurationsQuery query, CancellationToken cancellationToken)
    {
        var forms = await _configurationService.GetGroupedAsync(cancellationToken);
        return ApiResponse<List<FormFieldsDto>>.SuccessWithData(forms);
    }
}

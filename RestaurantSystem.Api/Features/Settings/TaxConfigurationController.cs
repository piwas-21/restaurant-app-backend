using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Settings.Dtos;
using RestaurantSystem.Api.Features.Settings.Interfaces;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Settings;

[ApiController]
[Route("api/[controller]")]
public class TaxConfigurationController : ControllerBase
{
    private readonly ITaxConfigurationService _taxConfigurationService;

    public TaxConfigurationController(ITaxConfigurationService taxConfigurationService)
    {
        _taxConfigurationService = taxConfigurationService;
    }

    private static List<OrderType> ParseApplicableOrderTypes(string? applicableOrderTypes)
    {
        if (string.IsNullOrWhiteSpace(applicableOrderTypes))
            return new List<OrderType>();

        return applicableOrderTypes
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => (OrderType)int.Parse(x.Trim()))
            .ToList();
    }

    private static string SerializeApplicableOrderTypes(List<OrderType> orderTypes)
    {
        if (orderTypes == null || orderTypes.Count == 0)
            return string.Empty;

        return string.Join(",", orderTypes.Select(ot => ((int)ot).ToString()));
    }

    [HttpGet]
    [Authorize(Roles = "Admin")]
    public async Task<ApiResponse<List<TaxConfigurationDto>>> GetAll(CancellationToken cancellationToken)
    {
        var taxConfigurations = await _taxConfigurationService.GetAllTaxConfigurationsAsync(cancellationToken);

        var dtos = taxConfigurations.Select(t => new TaxConfigurationDto
        {
            Id = t.Id,
            Name = t.Name,
            Rate = t.Rate,
            IsEnabled = t.IsEnabled,
            Description = t.Description,
            ApplicableOrderTypes = ParseApplicableOrderTypes(t.ApplicableOrderTypes)
        }).ToList();

        return ApiResponse<List<TaxConfigurationDto>>.SuccessWithData(dtos);
    }

    [HttpGet("active")]
    public async Task<ApiResponse<TaxConfigurationDto?>> GetActive(CancellationToken cancellationToken)
    {
        var taxConfiguration = await _taxConfigurationService.GetActiveTaxConfigurationAsync(cancellationToken);

        if (taxConfiguration == null)
            return ApiResponse<TaxConfigurationDto?>.SuccessWithData(null);

        var dto = new TaxConfigurationDto
        {
            Id = taxConfiguration.Id,
            Name = taxConfiguration.Name,
            Rate = taxConfiguration.Rate,
            IsEnabled = taxConfiguration.IsEnabled,
            Description = taxConfiguration.Description,
            ApplicableOrderTypes = ParseApplicableOrderTypes(taxConfiguration.ApplicableOrderTypes)
        };

        return ApiResponse<TaxConfigurationDto?>.SuccessWithData(dto);
    }

    [HttpGet("by-order-type/{orderType}")]
    public async Task<ApiResponse<TaxConfigurationDto?>> GetByOrderType(OrderType orderType, CancellationToken cancellationToken)
    {
        var taxConfiguration = await _taxConfigurationService.GetTaxConfigurationByOrderTypeAsync(orderType, cancellationToken);

        if (taxConfiguration == null)
            return ApiResponse<TaxConfigurationDto?>.SuccessWithData(null);

        var dto = new TaxConfigurationDto
        {
            Id = taxConfiguration.Id,
            Name = taxConfiguration.Name,
            Rate = taxConfiguration.Rate,
            IsEnabled = taxConfiguration.IsEnabled,
            Description = taxConfiguration.Description,
            ApplicableOrderTypes = ParseApplicableOrderTypes(taxConfiguration.ApplicableOrderTypes)
        };

        return ApiResponse<TaxConfigurationDto?>.SuccessWithData(dto);
    }

    [HttpGet("{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<ApiResponse<TaxConfigurationDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var taxConfiguration = await _taxConfigurationService.GetTaxConfigurationByIdAsync(id, cancellationToken);

        if (taxConfiguration == null)
            return ApiResponse<TaxConfigurationDto>.Failure("Tax configuration not found");

        var dto = new TaxConfigurationDto
        {
            Id = taxConfiguration.Id,
            Name = taxConfiguration.Name,
            Rate = taxConfiguration.Rate,
            IsEnabled = taxConfiguration.IsEnabled,
            Description = taxConfiguration.Description,
            ApplicableOrderTypes = ParseApplicableOrderTypes(taxConfiguration.ApplicableOrderTypes)
        };

        return ApiResponse<TaxConfigurationDto>.SuccessWithData(dto);
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<ApiResponse<TaxConfigurationDto>> Create(
        [FromBody] CreateTaxConfigurationDto dto,
        CancellationToken cancellationToken)
    {
        var taxConfiguration = new TaxConfiguration
        {
            Name = dto.Name,
            Rate = dto.Rate,
            IsEnabled = dto.IsEnabled,
            Description = dto.Description,
            ApplicableOrderTypes = SerializeApplicableOrderTypes(dto.ApplicableOrderTypes),
            CreatedBy = "Admin" // Will be set by service
        };

        var created = await _taxConfigurationService.CreateTaxConfigurationAsync(taxConfiguration, cancellationToken);

        var resultDto = new TaxConfigurationDto
        {
            Id = created.Id,
            Name = created.Name,
            Rate = created.Rate,
            IsEnabled = created.IsEnabled,
            Description = created.Description,
            ApplicableOrderTypes = ParseApplicableOrderTypes(created.ApplicableOrderTypes)
        };

        return ApiResponse<TaxConfigurationDto>.SuccessWithData(resultDto, "Tax configuration created successfully");
    }

    [HttpPut]
    [Authorize(Roles = "Admin")]
    public async Task<ApiResponse<TaxConfigurationDto>> Update(
        [FromBody] UpdateTaxConfigurationDto dto,
        CancellationToken cancellationToken)
    {
        var taxConfiguration = new TaxConfiguration
        {
            Id = dto.Id,
            Name = dto.Name,
            Rate = dto.Rate,
            IsEnabled = dto.IsEnabled,
            Description = dto.Description,
            ApplicableOrderTypes = SerializeApplicableOrderTypes(dto.ApplicableOrderTypes),
            CreatedBy = "System" // Will be preserved by service
        };

        try
        {
            var updated = await _taxConfigurationService.UpdateTaxConfigurationAsync(taxConfiguration, cancellationToken);

            var resultDto = new TaxConfigurationDto
            {
                Id = updated.Id,
                Name = updated.Name,
                Rate = updated.Rate,
                IsEnabled = updated.IsEnabled,
                Description = updated.Description,
                ApplicableOrderTypes = ParseApplicableOrderTypes(updated.ApplicableOrderTypes)
            };

            return ApiResponse<TaxConfigurationDto>.SuccessWithData(resultDto, "Tax configuration updated successfully");
        }
        catch (InvalidOperationException ex)
        {
            return ApiResponse<TaxConfigurationDto>.Failure(ex.Message);
        }
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<ApiResponse<bool>> Delete(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await _taxConfigurationService.DeleteTaxConfigurationAsync(id, cancellationToken);
            return ApiResponse<bool>.SuccessWithData(true, "Tax configuration deleted successfully");
        }
        catch (InvalidOperationException ex)
        {
            return ApiResponse<bool>.Failure(ex.Message);
        }
    }
}

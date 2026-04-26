using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Reservations.Dtos;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Reservations.Queries.ValidateTableQRCodeQuery;

public record ValidateTableQRCodeQuery(string QRCodeData) : IQuery<ApiResponse<TableValidationDto>>;

public class ValidateTableQRCodeQueryHandler : IQueryHandler<ValidateTableQRCodeQuery, ApiResponse<TableValidationDto>>
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<ValidateTableQRCodeQueryHandler> _logger;

    public ValidateTableQRCodeQueryHandler(ApplicationDbContext context, ILogger<ValidateTableQRCodeQueryHandler> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<ApiResponse<TableValidationDto>> Handle(ValidateTableQRCodeQuery query, CancellationToken cancellationToken)
    {
        try
        {
            var table = await _context.Tables
                .FirstOrDefaultAsync(t => t.QRCodeData == query.QRCodeData && t.IsActive, cancellationToken);

            if (table == null)
            {
                _logger.LogWarning("Invalid or inactive QR code scanned: {QRCodeData}", query.QRCodeData);
                return ApiResponse<TableValidationDto>.Failure("Invalid or inactive QR code");
            }

            var result = new TableValidationDto
            {
                IsValid = true,
                TableId = table.Id,
                TableNumber = table.TableNumber,
                MaxGuests = table.MaxGuests,
                IsOutdoor = table.IsOutdoor,
                QRCodeGeneratedAt = table.QRCodeGeneratedAt
            };

            _logger.LogInformation("Valid QR code scanned for table {TableNumber} (ID: {TableId})",
                table.TableNumber, table.Id);

            return ApiResponse<TableValidationDto>.SuccessWithData(result, "QR code is valid");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating QR code: {QRCodeData}", query.QRCodeData);
            return ApiResponse<TableValidationDto>.Failure("Failed to validate QR code");
        }
    }
}

using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Reservations.Dtos;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Reservations.Commands.GenerateTableQRCodeCommand;

public record GenerateTableQRCodeCommand(Guid TableId) : ICommand<ApiResponse<TableQRCodeDto>>;

public class GenerateTableQRCodeCommandHandler : ICommandHandler<GenerateTableQRCodeCommand, ApiResponse<TableQRCodeDto>>
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<GenerateTableQRCodeCommandHandler> _logger;

    public GenerateTableQRCodeCommandHandler(ApplicationDbContext context, ILogger<GenerateTableQRCodeCommandHandler> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<ApiResponse<TableQRCodeDto>> Handle(GenerateTableQRCodeCommand command, CancellationToken cancellationToken)
    {
        try
        {
            var table = await _context.Tables
                .FirstOrDefaultAsync(t => t.Id == command.TableId, cancellationToken);

            if (table == null)
            {
                return ApiResponse<TableQRCodeDto>.Failure("Table not found");
            }

            // Generate unique QR code data: table_{tableId}_{uniqueGuid}
            var qrCodeData = $"table_{table.Id}_{Guid.NewGuid():N}";

            table.QRCodeData = qrCodeData;
            table.QRCodeGeneratedAt = DateTime.UtcNow;
            table.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync(cancellationToken);

            var result = new TableQRCodeDto
            {
                TableId = table.Id,
                TableNumber = table.TableNumber,
                QRCodeData = table.QRCodeData,
                QRCodeGeneratedAt = table.QRCodeGeneratedAt.Value,
                // Generate full URL for scanning: https://yourapp.com/scan?qr={qrCodeData}
                QRCodeUrl = $"/scan?qr={qrCodeData}"
            };

            _logger.LogInformation("Generated QR code for table {TableNumber} (ID: {TableId})",
                table.TableNumber, table.Id);

            return ApiResponse<TableQRCodeDto>.SuccessWithData(result, "QR code generated successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating QR code for table {TableId}", command.TableId);
            return ApiResponse<TableQRCodeDto>.Failure("Failed to generate QR code");
        }
    }
}

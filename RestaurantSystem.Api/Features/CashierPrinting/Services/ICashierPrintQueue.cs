using RestaurantSystem.Api.Features.CashierPrinting.Dtos;

namespace RestaurantSystem.Api.Features.CashierPrinting.Services;

public interface ICashierPrintQueue
{
    Task<CashierPrintJobDto> QueueAsync(QueueCashierPrintRequest request, bool automatic,
        CancellationToken cancellationToken);
    Task<CashierPrintJobDto?> ClaimAsync(string deviceId, CancellationToken cancellationToken, bool allowAutomatic = true);
    Task<CashierPrintJobDto> ReadAsync(Guid jobId, CancellationToken cancellationToken);
    Task CompleteAsync(Guid jobId, string deviceId, string status, CancellationToken cancellationToken);
}

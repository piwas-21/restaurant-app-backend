using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.ServerWorkspace.Dtos;
using RestaurantSystem.Api.Features.ServerWorkspace.Services;

namespace RestaurantSystem.Api.Features.ServerWorkspace.Queries.GetServerTasksQuery;

public sealed record GetServerTasksQuery(
    string? Bucket = null,
    string? Cursor = null,
    int PageSize = 50) : IQuery<ApiResponse<ServerTaskFeedDto>>
{
    public string NormalizedBucket => Bucket?.Trim().ToLowerInvariant() ?? string.Empty;

    public string FilterHash(ICurrentUserService currentUser) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join(
            '\u001f',
            $"bucket={NormalizedBucket}",
            $"callerId={currentUser.UserId?.ToString("N", CultureInfo.InvariantCulture) ?? string.Empty}",
            $"staff={currentUser.IsStaff}"))))
        .ToLowerInvariant();
}

public sealed class GetServerTasksQueryHandler
    : IQueryHandler<GetServerTasksQuery, ApiResponse<ServerTaskFeedDto>>
{
    private readonly IServerTaskReader _reader;

    public GetServerTasksQueryHandler(IServerTaskReader reader) => _reader = reader;

    public async Task<ApiResponse<ServerTaskFeedDto>> Handle(
        GetServerTasksQuery query,
        CancellationToken cancellationToken)
    {
        if (query.PageSize is < 1 or > 100)
        {
            throw new BadRequestException("Task page size must be between 1 and 100.");
        }

        if (query.NormalizedBucket is not ("" or "ready" or "overdue" or "exception"))
        {
            throw new BadRequestException("Task bucket must be Ready, Overdue or Exception.");
        }

        return ApiResponse<ServerTaskFeedDto>.SuccessWithData(
            await _reader.ReadAsync(query, cancellationToken));
    }
}

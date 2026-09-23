using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.User.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.User.Queries.GetStaffCustomerLookupQuery;

public sealed record GetStaffCustomerLookupQuery(string Search, int PageSize = 10)
    : IQuery<ApiResponse<IReadOnlyList<StaffCustomerLookupDto>>>;

public sealed class GetStaffCustomerLookupQueryHandler
    : IQueryHandler<GetStaffCustomerLookupQuery, ApiResponse<IReadOnlyList<StaffCustomerLookupDto>>>
{
    private const int MinimumSearchLength = 2;
    private const int MaximumPageSize = 25;
    private readonly ApplicationDbContext _context;

    public GetStaffCustomerLookupQueryHandler(ApplicationDbContext context) => _context = context;

    public async Task<ApiResponse<IReadOnlyList<StaffCustomerLookupDto>>> Handle(
        GetStaffCustomerLookupQuery query, CancellationToken cancellationToken)
    {
        var search = query.Search?.Trim() ?? string.Empty;
        if (search.Length < MinimumSearchLength)
        {
            throw new BadRequestException(
                $"Customer search must contain at least {MinimumSearchLength} characters.");
        }

        var pageSize = Math.Clamp(query.PageSize, 1, MaximumPageSize);
        var pattern = $"%{EscapeLikePattern(search)}%";

        var customers = await _context.Users
            .Where(user => !user.IsDeleted
                && user.Role == UserRole.Customer
                && (EF.Functions.ILike(user.Email ?? string.Empty, pattern, "\\")
                    || EF.Functions.ILike(user.FirstName, pattern, "\\")
                    || EF.Functions.ILike(user.LastName, pattern, "\\")
                    || EF.Functions.ILike(user.FirstName + " " + user.LastName, pattern, "\\")))
            .OrderBy(user => user.FirstName)
            .ThenBy(user => user.LastName)
            .ThenBy(user => user.Email)
            .Take(pageSize)
            .Select(user => new StaffCustomerLookupDto
            {
                Id = user.Id,
                FirstName = user.FirstName,
                LastName = user.LastName,
                Email = user.Email ?? string.Empty,
                PhoneNumber = user.PhoneNumber,
                CurrentPoints = _context.FidelityPointBalances
                    .Where(balance => balance.UserId == user.Id)
                    .Select(balance => (int?)balance.CurrentPoints)
                    .FirstOrDefault() ?? 0
            })
            .ToListAsync(cancellationToken);

        return ApiResponse<IReadOnlyList<StaffCustomerLookupDto>>.SuccessWithData(
            customers, $"Found {customers.Count} active customer(s)");
    }

    private static string EscapeLikePattern(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
}

using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.User.Dtos;

namespace RestaurantSystem.Api.Features.User.Queries.GetUserStatisticsQuery;

public record GetUserStatisticsQuery() : IQuery<ApiResponse<UserStatisticsDto>>;

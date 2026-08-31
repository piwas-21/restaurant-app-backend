using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.RestaurantInfo.Dtos;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.RestaurantInfo.Commands.DeleteRestaurantInteriorImageCommand;

/// <summary>
/// Clears the uploaded custom landing-background image and deletes the stored file. Admin-only.
/// </summary>
/// <remarks>
/// Removing it has to be reachable: it makes custom mode unavailable but leaves the explicit
/// default and none landing-background states usable rather than inventing a separate image section.
/// </remarks>
public record DeleteRestaurantInteriorImageCommand : ICommand<ApiResponse<RestaurantInfoDto>>;

public class DeleteRestaurantInteriorImageCommandHandler
    : ICommandHandler<DeleteRestaurantInteriorImageCommand, ApiResponse<RestaurantInfoDto>>
{
    private readonly ApplicationDbContext _context;
    private readonly IFileStorageService _fileStorageService;
    private readonly ICurrentUserService _currentUserService;
    private readonly IConfiguration _configuration;

    public DeleteRestaurantInteriorImageCommandHandler(
        ApplicationDbContext context,
        IFileStorageService fileStorageService,
        ICurrentUserService currentUserService,
        IConfiguration configuration)
    {
        _context = context;
        _fileStorageService = fileStorageService;
        _currentUserService = currentUserService;
        _configuration = configuration;
    }

    public async Task<ApiResponse<RestaurantInfoDto>> Handle(
        DeleteRestaurantInteriorImageCommand command, CancellationToken cancellationToken)
    {
        var info = await _context.RestaurantInfo
            .Include(r => r.PhoneNumbers)
            .FirstOrDefaultAsync(cancellationToken);

        if (info is null)
        {
            throw new NotFoundException("Restaurant info has not been initialised.");
        }

        var storedUrl = info.InteriorImageUrl;

        // Clear the reference first and treat the file removal as best-effort — a storage
        // failure must not leave the page still showing a photo the admin asked to remove.
        info.InteriorImageUrl = null;
        info.UpdatedAt = DateTime.UtcNow;
        info.UpdatedBy = _currentUserService.GetAuditIdentifier();

        await _context.SaveChangesAsync(cancellationToken);

        if (!string.IsNullOrEmpty(storedUrl))
        {
            await _fileStorageService.DeleteFileAsync(storedUrl, cancellationToken);
        }

        return ApiResponse<RestaurantInfoDto>.SuccessWithData(
            RestaurantInfoMapper.ToDto(info, _configuration["AWS:S3:BaseUrl"]),
            "Interior image removed");
    }
}

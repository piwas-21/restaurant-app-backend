using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.RestaurantInfo.Commands.UpdateRestaurantLogoCommand;
using RestaurantSystem.Api.Features.RestaurantInfo.Dtos;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.RestaurantInfo.Commands.DeleteRestaurantLogoCommand;

/// <summary>
/// Clears one of the tenant's logos and deletes the stored file. Admin-only.
/// </summary>
/// <remarks>
/// Removing a logo has to be reachable, because the state it returns to is a real one: the
/// clients then render the restaurant's NAME as text, which is the designed default for a
/// restaurant that has no mark rather than an error state.
/// </remarks>
public record DeleteRestaurantLogoCommand(LogoVariant Variant)
    : ICommand<ApiResponse<RestaurantInfoDto>>;

public class DeleteRestaurantLogoCommandHandler
    : ICommandHandler<DeleteRestaurantLogoCommand, ApiResponse<RestaurantInfoDto>>
{
    private readonly ApplicationDbContext _context;
    private readonly IFileStorageService _fileStorageService;
    private readonly ICurrentUserService _currentUserService;
    private readonly IConfiguration _configuration;

    public DeleteRestaurantLogoCommandHandler(
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
        DeleteRestaurantLogoCommand command, CancellationToken cancellationToken)
    {
        var info = await _context.RestaurantInfo
            .Include(r => r.PhoneNumbers)
            .FirstOrDefaultAsync(cancellationToken);

        if (info is null)
        {
            throw new NotFoundException("Restaurant info has not been initialised.");
        }

        var storedUrl = UpdateRestaurantLogoCommandHandler.Read(info, command.Variant);

        // Clear the reference first and treat the file removal as best-effort. A storage
        // failure must not leave the row still pointing at a logo the admin asked to remove;
        // an orphaned file costs disk, a stuck logo costs the admin their intent.
        UpdateRestaurantLogoCommandHandler.Write(info, command.Variant, null);
        info.UpdatedAt = DateTime.UtcNow;
        info.UpdatedBy = _currentUserService.GetAuditIdentifier();

        await _context.SaveChangesAsync(cancellationToken);

        if (!string.IsNullOrEmpty(storedUrl))
        {
            await _fileStorageService.DeleteFileAsync(storedUrl, cancellationToken);
        }

        return ApiResponse<RestaurantInfoDto>.SuccessWithData(
            RestaurantInfoMapper.ToDto(info, _configuration["AWS:S3:BaseUrl"]),
            "Logo removed");
    }
}

using Microsoft.AspNetCore.Identity;
using RestaurantSystem.Domain.Common.Enums;
using System.Security.Claims;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Domain.Entities;


namespace RestaurantSystem.Api.Common.Services
{
    public class CurrentUserService : ICurrentUserService
    {

        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly UserManager<ApplicationUser> _userManager;

        public CurrentUserService(IHttpContextAccessor httpContextAccessor, UserManager<ApplicationUser> userManager)
        {
            _httpContextAccessor = httpContextAccessor;
            _userManager = userManager;
        }


        public Guid? UserId
        {
            get
            {
                var userId = _httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
                return !string.IsNullOrEmpty(userId) ? Guid.Parse(userId) : null;
            }
        }

        public string? UserName => _httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.Name);

        public string? Email => _httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.Email);

        public UserRole? Role
        {
            get
            {
                var roleString = _httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.Role);
                return !string.IsNullOrEmpty(roleString) && Enum.TryParse<UserRole>(roleString, out var role)
                    ? role
                    : null;
            }
        }

        public bool IsAuthenticated => _httpContextAccessor.HttpContext?.User.Identity?.IsAuthenticated ?? false;

        public bool IsAdmin => Role == UserRole.Admin;

        public async Task<ApplicationUser?> GetUserAsync()
        {
            if (!UserId.HasValue)
            {
                return null;
            }

            return await _userManager.FindByIdAsync(UserId.Value.ToString());
        }

    }
}

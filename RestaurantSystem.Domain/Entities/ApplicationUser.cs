using Microsoft.AspNetCore.Identity;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Common.Interfaces;

namespace RestaurantSystem.Domain.Entities
{
    public class ApplicationUser : IdentityUser<Guid>, IAuditable, ISoftDelete, IExcludeFromGlobalFilter
    {

        public required string FirstName { get; set; }
        public required string LastName { get; set; }

        public required UserRole Role { get; set; }

        public Dictionary<string, string> Metadata { get; set; } = [];

        // Audit properties
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public required string CreatedBy { get; set; }
        public string? UpdatedBy { get; set; }

        // Soft delete properties
        public bool IsDeleted { get; set; }
        public DateTime? DeletedAt { get; set; }
        public string? DeletedBy { get; set; }

        public required string RefreshToken { get; set; }
        public DateTime? RefreshTokenExpiryTime { get; set; }

        public decimal OrderLimitAmount { get; set; } // Order amount threshold
        public decimal DiscountPercentage { get; set; } // Percentage to apply when threshold is met
        public bool IsDiscountActive { get; set; }

        public DateTime? DeletionScheduledAt { get; set; }

        // When the last email-verification mail was sent to this address (UTC). Enforces the
        // per-address cooldown on /api/Auth/send-email-verification, which the per-IP rate limit
        // alone cannot do: an attacker rotating IPs would otherwise still bomb one known inbox.
        // Stamped on registration and on every accepted resend.
        public DateTime? LastEmailVerificationSentAt { get; set; }

        // Language this user's account mails are written in (EMAIL-LOCALISATION-PLAN §1 rank 2).
        // Canonical primary subtag or null; null means "never expressed one" and falls through to
        // the request header, then the tenant default. Editable, so it follows the user's choice.
        public string? PreferredLanguage { get; set; }

        public virtual ICollection<UserAddress> Addresses { get; set; } = new List<UserAddress>();
    }
}

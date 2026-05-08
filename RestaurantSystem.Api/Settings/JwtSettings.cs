using Microsoft.IdentityModel.Tokens;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace RestaurantSystem.Api.Settings
{
    /// <summary>
    /// Configuration settings for JWT (JSON Web Token) authentication.
    /// This class holds all the necessary parameters for generating and validating JWT tokens.
    /// </summary>
    public class JwtSettings
    {
        /// <summary>
        /// Gets or sets the secret key used for token signing.
        /// This should be a strong, random key of sufficient length (at least 32 characters recommended).
        /// </summary>
        [Required]
        public string Secret { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the issuer of the token (typically your application name or URL).
        /// This is used to validate the token's issuer during validation.
        /// </summary>
        [Required]
        public string Issuer { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the audience of the token (typically your application name or client ID).
        /// This is used to validate the token's audience during validation.
        /// </summary>
        [Required]
        public string Audience { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the expiry time for access tokens in minutes.
        /// Default is 60 minutes (1 hour).
        /// </summary>
        public int ExpiryMinutes { get; set; } = 60;

        /// <summary>
        /// Gets or sets the expiry time for refresh tokens in days.
        /// Default is 7 days.
        /// </summary>
        public int RefreshTokenExpiryDays { get; set; } = 7;

        /// <summary>
        /// Gets or sets whether the token requires HTTPS.
        /// In production, this should be true.
        /// </summary>
        public bool RequireHttpsMetadata { get; set; } = true;

        /// <summary>
        /// Gets or sets whether to save the token after validation.
        /// </summary>
        public bool SaveToken { get; set; } = true;

        /// <summary>
        /// Gets or sets the clock skew to allow for server time differences.
        /// Default is 0 minutes for stricter validation.
        /// </summary>
        public int ClockSkewMinutes { get; set; }

        /// <summary>
        /// Gets or sets whether to validate the token lifetime.
        /// This should typically be true in production.
        /// </summary>
        public bool ValidateLifetime { get; set; } = true;

        /// <summary>
        /// Gets or sets whether to validate the token issuer.
        /// This should typically be true in production.
        /// </summary>
        public bool ValidateIssuer { get; set; } = true;

        /// <summary>
        /// Gets or sets whether to validate the token audience.
        /// This should typically be true in production.
        /// </summary>
        public bool ValidateAudience { get; set; } = true;

        /// <summary>
        /// Gets or sets whether to validate the token signing key.
        /// This should always be true.
        /// </summary>
        public bool ValidateIssuerSigningKey { get; set; } = true;

        /// <summary>
        /// Gets the security key derived from the Secret property.
        /// </summary>
        public SymmetricSecurityKey SecurityKey => new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret));


        /// <summary>
        /// Gets the signing credentials used for token generation.
        /// </summary>
        public SigningCredentials SigningCredentials => new SigningCredentials(SecurityKey, SecurityAlgorithms.HmacSha256);


        /// <summary>
        /// Gets the TokenValidationParameters built from these settings.
        /// </summary>
        public TokenValidationParameters TokenValidationParameters => new TokenValidationParameters
        {
            ValidateIssuer = ValidateIssuer,
            ValidateAudience = ValidateAudience,
            ValidateLifetime = ValidateLifetime,
            ValidateIssuerSigningKey = ValidateIssuerSigningKey,
            ValidIssuer = Issuer,
            ValidAudience = Audience,
            IssuerSigningKey = SecurityKey,
            ClockSkew = TimeSpan.FromMinutes(ClockSkewMinutes),
            RequireExpirationTime = true
        };

        /// <summary>
        /// Gets the TokenValidationParameters for refreshing tokens.
        /// </summary>
        public TokenValidationParameters RefreshTokenValidationParameters => new TokenValidationParameters
        {
            ValidateIssuer = ValidateIssuer,
            ValidateAudience = ValidateAudience,
            ValidateLifetime = false, // Don't validate lifetime when refreshing
            ValidateIssuerSigningKey = ValidateIssuerSigningKey,
            ValidIssuer = Issuer,
            ValidAudience = Audience,
            IssuerSigningKey = SecurityKey,
            RequireExpirationTime = true
        };

        /// <summary>
        /// Validates the settings to ensure they're properly configured.
        /// </summary>
        public void Validate()
        {
            if (string.IsNullOrEmpty(Secret))
                throw new InvalidOperationException("JWT Secret must be configured");

            if (Secret.Length < 32)
                throw new InvalidOperationException("JWT Secret should be at least 32 characters long");

            if (string.IsNullOrEmpty(Issuer))
                throw new InvalidOperationException("JWT Issuer must be configured");

            if (string.IsNullOrEmpty(Audience))
                throw new InvalidOperationException("JWT Audience must be configured");

            if (ExpiryMinutes <= 0)
                throw new InvalidOperationException("JWT ExpiryMinutes must be greater than zero");

            if (RefreshTokenExpiryDays <= 0)
                throw new InvalidOperationException("JWT RefreshTokenExpiryDays must be greater than zero");
        }

    }
}

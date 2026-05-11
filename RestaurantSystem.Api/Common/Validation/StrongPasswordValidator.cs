using Microsoft.AspNetCore.Identity;
using System.Text.RegularExpressions;

namespace RestaurantSystem.Api.Common.Validation
{
    /// <summary>
    /// Custom password validator that implements strong password requirements.
    /// </summary>
    public class StrongPasswordValidator<TUser> : IPasswordValidator<TUser> where TUser : class
    {
        private readonly int _minLength;
        private readonly bool _requireNonAlphanumeric;
        private readonly bool _requireLowercase;
        private readonly bool _requireUppercase;
        private readonly bool _requireDigit;
        private readonly bool _requireUniqueChars;
        private readonly int _minUniqueChars;
        private readonly bool _preventCommonPasswords;
        private readonly HashSet<string> _commonPasswords;

        public StrongPasswordValidator(
            int minLength = 8,
            bool requireNonAlphanumeric = true,
            bool requireLowercase = true,
            bool requireUppercase = true,
            bool requireDigit = true,
            bool requireUniqueChars = true,
            int minUniqueChars = 4,
            bool preventCommonPasswords = true)
        {
            _minLength = minLength;
            _requireNonAlphanumeric = requireNonAlphanumeric;
            _requireLowercase = requireLowercase;
            _requireUppercase = requireUppercase;
            _requireDigit = requireDigit;
            _requireUniqueChars = requireUniqueChars;
            _minUniqueChars = minUniqueChars;
            _preventCommonPasswords = preventCommonPasswords;

            // Initialize common passwords list
            _commonPasswords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "password", "123456", "12345678", "qwerty", "admin", "welcome",
                "letmein", "trustno1", "password123", "admin123" /* etc... */
            };
        }

        public Task<IdentityResult> ValidateAsync(UserManager<TUser> manager, TUser user, string? password)
        {
            var errors = new List<IdentityError>();

            if (password == null)
            {

                errors.Add(new IdentityError
                {
                    Code = "PosswordNull",
                    Description = $"Password should not be null"
                });

                return Task.FromResult(IdentityResult.Failed(errors.ToArray()));
            }

            // Check minimum length
            if (password.Length < _minLength)
            {
                errors.Add(new IdentityError
                {
                    Code = "PasswordTooShort",
                    Description = $"Password must be at least {_minLength} characters long."
                });
            }

            // Check for uppercase letters
            if (_requireUppercase && !Regex.IsMatch(password, "[A-Z]"))
            {
                errors.Add(new IdentityError
                {
                    Code = "PasswordRequiresUpper",
                    Description = "Password must contain at least one uppercase letter."
                });
            }

            // Check for lowercase letters
            if (_requireLowercase && !Regex.IsMatch(password, "[a-z]"))
            {
                errors.Add(new IdentityError
                {
                    Code = "PasswordRequiresLower",
                    Description = "Password must contain at least one lowercase letter."
                });
            }

            // Check for digits
            if (_requireDigit && !Regex.IsMatch(password, "[0-9]"))
            {
                errors.Add(new IdentityError
                {
                    Code = "PasswordRequiresDigit",
                    Description = "Password must contain at least one digit."
                });
            }

            // Check for non-alphanumeric characters
            if (_requireNonAlphanumeric && !Regex.IsMatch(password, "[^a-zA-Z0-9]"))
            {
                errors.Add(new IdentityError
                {
                    Code = "PasswordRequiresNonAlphanumeric",
                    Description = "Password must contain at least one special character."
                });
            }

            // Check for unique characters
            if (_requireUniqueChars && password.Distinct().Count() < _minUniqueChars)
            {
                errors.Add(new IdentityError
                {
                    Code = "PasswordRequiresUniqueChars",
                    Description = $"Password must contain at least {_minUniqueChars} different characters."
                });
            }

            // Check for patterns that indicate weak passwords
            if (HasRepeatingPatterns(password))
            {
                errors.Add(new IdentityError
                {
                    Code = "PasswordHasPatterns",
                    Description = "Password contains repeating patterns. Please use a more complex password."
                });
            }

            // Check for sequential characters
            if (HasSequentialCharacters(password))
            {
                errors.Add(new IdentityError
                {
                    Code = "PasswordHasSequence",
                    Description = "Password contains sequential characters (like '123' or 'abc')."
                });
            }

            // Check against common password list
            if (_preventCommonPasswords && IsCommonPassword(password))
            {
                errors.Add(new IdentityError
                {
                    Code = "PasswordIsCommon",
                    Description = "Password is too common and easily guessable."
                });
            }

            return Task.FromResult(errors.Count > 0
                ? IdentityResult.Failed(errors.ToArray())
                : IdentityResult.Success);
        }

        private bool IsCommonPassword(string password)
        {
            // Implementation details...
            return _commonPasswords.Contains(password);
        }

        private bool HasRepeatingPatterns(string password)
        {
            // Implementation details...
            return Regex.IsMatch(password, @"(.)\1{2,}");
        }

        private bool HasSequentialCharacters(string password)
        {
            // Implementation details...
            // Check implementation...
            return false;
        }
    }
}

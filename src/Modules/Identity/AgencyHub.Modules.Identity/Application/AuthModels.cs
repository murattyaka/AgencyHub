using AgencyHub.Modules.Identity.Contracts;
using AgencyHub.Modules.Identity.Domain;
using FluentValidation;

namespace AgencyHub.Modules.Identity.Application;

/// <param name="Email">Sign-in email.</param>
/// <param name="Password">Plain-text password; never logged or persisted.</param>
/// <param name="AgencyId">Required only when the user belongs to more than one agency. Validated against the user's memberships.</param>
internal sealed record LoginRequest(string Email, string Password, Guid? AgencyId);

internal sealed record AuthResult(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAtUtc,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAtUtc,
    Guid AgencyId,
    AgencyRole Role);

internal sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        // No format checks here: login must not reveal anything about account rules.
        RuleFor(x => x.Email).NotEmpty().MaximumLength(User.EmailMaxLength);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(PasswordPolicy.MaximumLength);
    }
}

internal static class PasswordPolicy
{
    public const int MinimumLength = 12;
    public const int MaximumLength = 128;
}

internal sealed class CreateAgencyUserRequestValidator : AbstractValidator<CreateAgencyUserRequest>
{
    public CreateAgencyUserRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(User.EmailMaxLength);
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(User.DisplayNameMaxLength);
        RuleFor(x => x.Role).IsInEnum();
        RuleFor(x => x.Password)
            .NotEmpty()
            .MinimumLength(PasswordPolicy.MinimumLength)
            .MaximumLength(PasswordPolicy.MaximumLength)
            .Must((request, password) => !string.Equals(password, request.Email, StringComparison.OrdinalIgnoreCase))
            .WithMessage("Password must not be the same as the email address.");
    }
}

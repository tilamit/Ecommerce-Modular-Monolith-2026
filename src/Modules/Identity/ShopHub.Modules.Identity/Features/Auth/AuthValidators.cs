using FluentValidation;

namespace ShopHub.Modules.Identity.Features.Auth;

internal sealed class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    /// <summary>
    /// Length is the property that actually correlates with password strength, so the
    /// floor is 8 rather than a composition rule that mostly produces "Password1!".
    /// </summary>
    internal const int MinimumPasswordLength = 8;

    internal const int MaximumPasswordLength = 128;

    public RegisterRequestValidator()
    {
        RuleFor(r => r.Email)
            .NotEmpty()
            .MaximumLength(256)
            .EmailAddress();

        RuleFor(r => r.Password)
            .NotEmpty()
            .MinimumLength(MinimumPasswordLength)
            .MaximumLength(MaximumPasswordLength);

        RuleFor(r => r.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(r => r.LastName).NotEmpty().MaximumLength(100);
        RuleFor(r => r.PhoneNumber).MaximumLength(32);
    }
}

internal sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        // Deliberately no format or length rules beyond "present": a validation message
        // that differs by input is another way to leak which accounts exist.
        RuleFor(r => r.Email).NotEmpty().MaximumLength(256);
        RuleFor(r => r.Password).NotEmpty().MaximumLength(RegisterRequestValidator.MaximumPasswordLength);
    }
}

using FluentValidation;
using ShopHub.Modules.Identity.Features.Auth;

namespace ShopHub.Modules.Identity.Features.Users;

internal sealed class CreateUserRequestValidator : AbstractValidator<CreateUserRequest>
{
    public CreateUserRequestValidator()
    {
        RuleFor(r => r.Email).NotEmpty().MaximumLength(256).EmailAddress();

        RuleFor(r => r.Password)
            .NotEmpty()
            .MinimumLength(RegisterRequestValidator.MinimumPasswordLength)
            .MaximumLength(RegisterRequestValidator.MaximumPasswordLength);

        RuleFor(r => r.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(r => r.LastName).NotEmpty().MaximumLength(100);
        RuleFor(r => r.PhoneNumber).MaximumLength(32);
        RuleFor(r => r.RoleIds).NotEmpty().WithMessage("A user must have at least one role.");
    }
}

internal sealed class UpdateUserRequestValidator : AbstractValidator<UpdateUserRequest>
{
    public UpdateUserRequestValidator()
    {
        RuleFor(r => r.Email).NotEmpty().MaximumLength(256).EmailAddress();
        RuleFor(r => r.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(r => r.LastName).NotEmpty().MaximumLength(100);
        RuleFor(r => r.PhoneNumber).MaximumLength(32);
    }
}

internal sealed class UpdateMyProfileRequestValidator : AbstractValidator<UpdateMyProfileRequest>
{
    public UpdateMyProfileRequestValidator()
    {
        RuleFor(r => r.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(r => r.LastName).NotEmpty().MaximumLength(100);
        RuleFor(r => r.PhoneNumber).MaximumLength(32);
    }
}

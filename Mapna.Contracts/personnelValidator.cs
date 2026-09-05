using FluentValidation;

namespace Mapna.Contracts;

public class PersonnelValidator : AbstractValidator<PersonnelRecord>
{
    public PersonnelValidator()
    {
        RuleFor(x => x.PerId)
        .GreaterThan(0)
        .WithMessage("PerId باید بزرگ‌تر از صفر باشد.");

        RuleFor(x => x.PerName)
            .NotEmpty()
            .WithMessage("نام نمی‌تواند خالی باشد.");

        RuleFor(x => x.PerSurname)
            .NotEmpty()
            .WithMessage("نام‌خانوادگی نمی‌تواند خالی باشد.");

        RuleFor(x => x.NationalCode)
            .Must(IranianNationalCodeValidator.IsValid)
            .WithMessage("کد ملی معتبر نیست.");

        RuleFor(x => x.PerEmail)
            .EmailAddress()
            .When(x => !string.IsNullOrWhiteSpace(x.PerEmail))
            .WithMessage("فرمت ایمیل معتبر نیست.");

        RuleFor(x => x.MobileNo)
            .Matches(@"^09\d{9}$")
            .When(x => !string.IsNullOrWhiteSpace(x.MobileNo))
            .WithMessage("فرمت موبایل معتبر نیست.");
    }
}

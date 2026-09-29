using FluentValidation;

namespace Mapna.Contracts;

/// <summary>
/// The ONLY validation contract for a personnel record. The sender uses it to decide what to send and the
/// receiver re-applies it (ASP.NET's implicit [Required] is switched off there so no second rule set exists).
/// </summary>
public class PersonnelValidator : AbstractValidator<PersonnelRecord>
{
    public PersonnelValidator()
    {
        RuleFor(x => x.PerId)
            .GreaterThan(0)
            .WithMessage("PerId باید بزرگ‌تر از صفر باشد.");

        RuleFor(x => x.PerName)
            .NotEmpty().WithMessage("نام نمی‌تواند خالی باشد.")
            .MaximumLength(PersonnelFieldLimits.Name).WithMessage($"نام نباید بیش از {PersonnelFieldLimits.Name} کاراکتر باشد.");

        RuleFor(x => x.PerSurname)
            .NotEmpty().WithMessage("نام‌خانوادگی نمی‌تواند خالی باشد.")
            .MaximumLength(PersonnelFieldLimits.Name).WithMessage($"نام‌خانوادگی نباید بیش از {PersonnelFieldLimits.Name} کاراکتر باشد.");

        RuleFor(x => x.PerLName)
            .MaximumLength(PersonnelFieldLimits.Name).WithMessage($"نام لاتین نباید بیش از {PersonnelFieldLimits.Name} کاراکتر باشد.");

        RuleFor(x => x.PerLSurname)
            .MaximumLength(PersonnelFieldLimits.Name).WithMessage($"نام‌خانوادگی لاتین نباید بیش از {PersonnelFieldLimits.Name} کاراکتر باشد.");

        RuleFor(x => x.NationalCode)
            .Must(IranianNationalCodeValidator.IsValid)
            .WithMessage("کد ملی معتبر نیست.");

        // WithMessage binds to the rule immediately before it, so each rule gets its own.
        RuleFor(x => x.PerEmail)
            .EmailAddress().WithMessage("فرمت ایمیل معتبر نیست.")
            .MaximumLength(PersonnelFieldLimits.Email).WithMessage($"ایمیل نباید بیش از {PersonnelFieldLimits.Email} کاراکتر باشد.")
            .When(x => !string.IsNullOrWhiteSpace(x.PerEmail));

        // [0-9], not \d: in .NET, \d matches every Unicode decimal digit, so "09۱۲۳۴۵۶۷۸۹" used to pass.
        RuleFor(x => x.MobileNo)
            .Matches("^09[0-9]{9}$")
            .When(x => !string.IsNullOrWhiteSpace(x.MobileNo))
            .WithMessage("فرمت موبایل معتبر نیست.");

        RuleFor(x => x.SexCode).MaximumLength(PersonnelFieldLimits.SexCode);
        RuleFor(x => x.Phone).MaximumLength(PersonnelFieldLimits.Phone);
        RuleFor(x => x.PerAddr).MaximumLength(PersonnelFieldLimits.Address);
        RuleFor(x => x.BornDate).MaximumLength(PersonnelFieldLimits.BornDate);
        RuleFor(x => x.UserPrincipalName).MaximumLength(PersonnelFieldLimits.UserPrincipalName);
        RuleFor(x => x.CompanyId).MaximumLength(PersonnelFieldLimits.CompanyId);
        RuleFor(x => x.PerContract).MaximumLength(PersonnelFieldLimits.PerContract);
    }
}

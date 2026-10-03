using Mapna.Contracts;

namespace Mapna.Tests;

public class ValidationTests
{
    private readonly PersonnelValidator _validator = new();

    [Fact]
    public void Generated_national_codes_are_valid() =>
        Assert.All(Enumerable.Range(1, 500), i => Assert.True(IranianNationalCodeValidator.IsValid(TestData.NationalCode(i))));

    [Fact]
    public void Persian_digits_are_rejected_by_the_national_code_validator()
    {
        var ascii = TestData.NationalCode(42);
        var persian = new string(ascii.Select(c => (char)('۰' + (c - '0'))).ToArray());
        Assert.True(IranianNationalCodeValidator.IsValid(ascii));
        Assert.False(IranianNationalCodeValidator.IsValid(persian));
    }

    [Fact]
    public void Persian_digit_mobile_no_longer_passes_validation()
    {
        // Old rule was ^09\d{9}$ and .NET's \d matches Persian digits, so a mixed value (ASCII "09" prefix, as typed
        // on a Persian keyboard after an auto-filled prefix) was VALID and got sent to the destination as-is.
        Assert.Matches(@"^09\d{9}$", "09۱۲۳۴۵۶۷۸۹");
        var record = TestData.Record(1, r => r.MobileNo = "09۱۲۳۴۵۶۷۸۹");
        Assert.False(_validator.Validate(record).IsValid);
    }

    [Fact]
    public void Normalizer_turns_persian_digits_into_ascii_and_then_the_record_is_valid()
    {
        var record = TestData.Record(1, r =>
        {
            r.MobileNo = " ۰۹۱۲۳۴۵۶۷۸۹ ";
            r.NationalCode = new string(TestData.NationalCode(1).Select(c => (char)('۰' + (c - '0'))).ToArray());
            r.PerAddr = "NULL";
            r.PerName = "  علی  ";
        });

        PersonnelNormalizer.Normalize(record, normalizeArabicLetters: false);

        Assert.Equal("09123456789", record.MobileNo);
        Assert.Equal(TestData.NationalCode(1), record.NationalCode);
        Assert.Null(record.PerAddr);
        Assert.Equal("علی", record.PerName);
        Assert.True(_validator.Validate(record).IsValid);
    }

    [Fact]
    public void Arabic_letters_are_only_rewritten_when_enabled()
    {
        var arabic = "علي كريمي";
        Assert.Equal(arabic, PersonnelNormalizer.Text(arabic, normalizeArabicLetters: false));
        Assert.Equal("علی کریمی", PersonnelNormalizer.Text(arabic, normalizeArabicLetters: true));
    }

    [Fact]
    public void Empty_latin_names_are_valid_for_the_shared_contract() =>
        Assert.True(_validator.Validate(TestData.Record(1, r => { r.PerLName = ""; r.PerLSurname = ""; r.SexCode = ""; })).IsValid);

    [Fact]
    public void Over_long_values_are_rejected_before_they_can_hit_a_column_limit() =>
        Assert.False(_validator.Validate(TestData.Record(1, r => r.PerName = new string('x', PersonnelFieldLimits.Name + 1))).IsValid);
}

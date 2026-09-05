namespace Mapna.Contracts;

public class IranianNationalCodeValidator
{
    public static bool IsValid(string? nationalCode)
    {
        if (string.IsNullOrWhiteSpace(nationalCode) || nationalCode.Length != 10)
            return false;

        if (!nationalCode.All(char.IsDigit))
            return false;

        if (nationalCode.Distinct().Count() == 1)
            return false;

        var digits = nationalCode.Select(c => c - '0').ToArray();
        var checkDigit = digits[9];

        var sum = 0;
        for (int i = 0; i < 9; i++)
            sum += digits[i] * (10 - i);

        var remainder = sum % 11;

        return remainder < 2
            ? checkDigit == remainder
            : checkDigit == 11 - remainder;
    }

}

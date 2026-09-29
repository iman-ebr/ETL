namespace Mapna.Contracts;

public static class IranianNationalCodeValidator
{
    public static bool IsValid(string? nationalCode)
    {
        if (string.IsNullOrEmpty(nationalCode) || nationalCode.Length != 10)
            return false;

        // ASCII digits only. char.IsDigit() also accepts Persian/Arabic-Indic digits (U+06F0..U+06F9, U+0660..U+0669),
        // which then break the (c - '0') arithmetic below. Non-ASCII digits must be normalized at the source edge
        // (PersonnelNormalizer), never silently accepted here.
        foreach (var c in nationalCode)
        {
            if (c is < '0' or > '9')
                return false;
        }

        if (nationalCode.AsSpan().IndexOfAnyExcept(nationalCode[0]) < 0)
            return false;

        var sum = 0;
        for (var i = 0; i < 9; i++)
            sum += (nationalCode[i] - '0') * (10 - i);

        var remainder = sum % 11;
        var checkDigit = nationalCode[9] - '0';

        return remainder < 2
            ? checkDigit == remainder
            : checkDigit == 11 - remainder;
    }
}

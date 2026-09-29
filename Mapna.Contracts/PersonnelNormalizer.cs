using System.Text;

namespace Mapna.Contracts;

/// <summary>
/// Canonicalizes a source record once, at the edge where it enters the pipeline (SourceRepository).
/// Everything downstream (validation, snapshot diffing, the receiver) then sees one canonical form.
///
/// NOTE: turning this on changes the canonical value of some records (trimmed whitespace, ASCII digits),
/// so the first run after deployment will detect and send those changes once. That's expected and shows up
/// in the audit log with ChangedFields. Make sure the business has signed off before going live.
/// </summary>
public static class PersonnelNormalizer
{
    public static void Normalize(PersonnelRecord r, bool normalizeArabicLetters)
    {
        r.PerName = Text(r.PerName, normalizeArabicLetters) ?? string.Empty;
        r.PerSurname = Text(r.PerSurname, normalizeArabicLetters) ?? string.Empty;
        r.PerLName = Text(r.PerLName, false) ?? string.Empty;
        r.PerLSurname = Text(r.PerLSurname, false) ?? string.Empty;
        r.SexCode = Text(r.SexCode, false) ?? string.Empty;
        r.PerAddr = Text(r.PerAddr, normalizeArabicLetters);
        r.PerEmail = Text(r.PerEmail, false);
        r.UserPrincipalName = Text(r.UserPrincipalName, false);
        r.CompanyId = Text(r.CompanyId, false);
        r.PerContract = Text(r.PerContract, false);
        r.BornDate = Digits(Text(r.BornDate, false));

        r.NationalCode = Digits(Text(r.NationalCode, false)) ?? string.Empty;
        r.MobileNo = Digits(Text(r.MobileNo, false));
        r.Phone = Digits(Text(r.Phone, false));
    }

    /// <summary>Trim; map empty / whitespace / literal "NULL" to null; optionally map Arabic Yeh/Kaf to Persian.</summary>
    public static string? Text(string? value, bool normalizeArabicLetters)
    {
        if (value is null) return null;
        var trimmed = value.Trim();
        if (trimmed.Length == 0 || trimmed.Equals("NULL", StringComparison.OrdinalIgnoreCase))
            return null;

        return normalizeArabicLetters
            ? trimmed.Replace('ي', 'ی').Replace('ى', 'ی').Replace('ك', 'ک')
            : trimmed;
    }

    /// <summary>Persian (U+06F0..U+06F9) and Arabic-Indic (U+0660..U+0669) digits to ASCII.</summary>
    public static string? Digits(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value;

        StringBuilder? sb = null;
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            var mapped = c switch
            {
                >= '۰' and <= '۹' => (char)('0' + (c - '۰')),
                >= '٠' and <= '٩' => (char)('0' + (c - '٠')),
                _ => c
            };
            if (mapped != c)
            {
                sb ??= new StringBuilder(value);
                sb[i] = mapped;
            }
        }
        return sb?.ToString() ?? value;
    }
}

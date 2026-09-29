using Mapna.Contracts;

namespace Mapna.Tests;

internal static class TestData
{
    /// <summary>Builds a checksum-valid Iranian national code from a 9-digit body.</summary>
    public static string NationalCode(int seed)
    {
        var body = (100_000_000 + (seed * 7919 % 800_000_000)).ToString("D9");
        var sum = 0;
        for (var i = 0; i < 9; i++) sum += (body[i] - '0') * (10 - i);
        var r = sum % 11;
        var check = r < 2 ? r : 11 - r;
        return body + check;
    }

    public static PersonnelRecord Record(int perId, Action<PersonnelRecord>? tweak = null)
    {
        var r = new PersonnelRecord
        {
            PerId = perId,
            PerName = "علی",
            PerSurname = "رضایی",
            PerLName = "Ali",
            PerLSurname = "Rezaei",
            PerStatus = 1,
            SexCode = "M",
            NationalCode = NationalCode(perId),
            MobileNo = "0912" + (1_000_000 + perId % 9_000_000).ToString("D7"),
            PerEmail = $"user{perId}@example.test",
            CompanyId = "10"
        };
        tweak?.Invoke(r);
        return r;
    }
}

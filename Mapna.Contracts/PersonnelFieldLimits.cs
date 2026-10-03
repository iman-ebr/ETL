namespace Mapna.Contracts;

/// <summary>
/// Single source of truth for field lengths. Used by <see cref="PersonnelValidator"/> (sender + receiver)
/// and by the EF model of the destination table, so a value that passes validation always fits the column.
/// NationalCode and MobileNo are fixed by their format. The rest are upper bounds that MUST be confirmed
/// against the real destination system's schema before go-live.
/// </summary>
public static class PersonnelFieldLimits
{
    public const int NationalCode = 10;
    public const int MobileNo = 11;

    public const int Name = 100;          // PerName, PerSurname, PerLName, PerLSurname
    public const int SexCode = 10;
    public const int Email = 256;
    public const int Phone = 30;
    public const int Address = 500;
    public const int BornDate = 20;
    public const int UserPrincipalName = 256;
    public const int CompanyId = 50;
    public const int PerContract = 50;
}

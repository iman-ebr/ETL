using Mapna.Contracts;
using System.Runtime.CompilerServices;

namespace Mapna.LogData;

//For Map Personnel between Receiver and sender
public static class PersonnelMapper
{
    public static void ApplyTo(this PersonnelRecord source,Personnel target)
    {
        target.PerId = source.PerId;
        target.PerName = source.PerName;
        target.PerSurname = source.PerSurname;
        target.PerStatus = source.PerStatus;
        target.SexCode = source.SexCode;
        target.PerEmail = source.PerEmail;
        target.MobileNo = source.MobileNo;
        target.Phone = source.Phone;
        target.PerAddr = source.PerAddr;
        target.PerLName = source.PerLName;
        target.PerLSurname = source.PerLSurname;
        target.BornDate = source.BornDate;
        target.NationalCode = source.NationalCode;
        target.UserPrincipalName = source.UserPrincipalName;
        target.CompanyId = source.CompanyId;
        target.PerContract = source.PerContract;
        target.LastUpdatedUtc = DateTime.UtcNow;
    }
}
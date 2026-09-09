using Mapna.Contracts;
using Microsoft.Data.SqlClient;
using System.Data;
using Dapper;

namespace Mapna.Sender;

public class SourceRepository
{
    private readonly string _connectionString;

    public SourceRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public IReadOnlyList<PersonnelRecord> GetAllPersonnel()
    {
        const string query = @"
            SELECT
                PER_ID              AS PerId,
                PER_NAME            AS PerName,
                PER_SURNAME         AS PerSurname,
                PER_STATUS          AS PerStatus,
                SEX_CODE            AS SexCode,
                PER_EMAIL           AS PerEmail,
                MOBIL_NO            AS MobileNo,
                PHONE               AS Phone,
                PER_ADDR            AS PerAddr,
                PER_LNAME           AS PerLName,
                PER_LSURNAME        AS PerLSurname,
                BORN_DATE           AS BornDate,
                NATIONAL_CODE       AS NationalCode,
                USER_PRINCIPAL_NAME AS UserPrincipalName,
                PER_CONTRACT        AS PerContract,
                COMPANY_ID          AS CompanyId
            FROM PERSONEL_Sender";

        using IDbConnection connection = new SqlConnection(_connectionString);
        var records = connection.Query<PersonnelRecord>(query).ToList();

        foreach (var record in records)
        {
            record.Phone = NormalizeNull(record.Phone);
            record.PerAddr = NormalizeNull(record.PerAddr);
            record.PerEmail = NormalizeNull(record.PerEmail);
            record.MobileNo = NormalizeNull(record.MobileNo);
            record.UserPrincipalName = NormalizeNull(record.UserPrincipalName);
            record.PerContract = NormalizeNull(record.PerContract);
            record.BornDate = NormalizeNull(record.BornDate);
        }

        return records;
    }
    private static string? NormalizeNull(string? value) =>
    string.IsNullOrWhiteSpace(value) || value.Trim().Equals("NULL", StringComparison.OrdinalIgnoreCase)
        ? null
        : value;
}
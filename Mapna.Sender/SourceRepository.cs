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
                p.PER_ID              AS PerId,
                p.PER_NAME            AS PerName,
                p.PER_SURNAME         AS PerSurname,
                p.PER_STATUS          AS PerStatus,
                p.SEX_CODE            AS SexCode,
                p.PER_EMAIL           AS PerEmail,
                p.MOBIL_NO            AS MobileNo,
                p.PHONE               AS Phone,
                p.PER_ADDR            AS PerAddr,
                p.PER_LNAME           AS PerLName,
                p.PER_LSURNAME        AS PerLSurname,
                p.BORN_DATE           AS BornDate,
                p.NATIONAL_CODE       AS NationalCode,
                p.USER_PRINCIPAL_NAME AS UserPrincipalName,
                p.PER_CONTRACT        AS PerContract,
                a.COMPANY_ID          AS CompanyId
            FROM PERSONEL p
            OUTER APPLY (
                SELECT TOP 1 a2.COMPANY_ID
                FROM ASSIGNMENT a2
                WHERE a2.PER_ID = p.PER_ID
                ORDER BY
                    CASE WHEN a2.ASSIGNMENT_DISCHARGE_DATE IS NULL THEN 0 ELSE 1 END,
                    a2.ASSIGNMENT_ASSIGN_DATE DESC
            ) a";

        using IDbConnection connection = new SqlConnection(_connectionString);
        return connection.Query<PersonnelRecord>(query).ToList();
    }
}
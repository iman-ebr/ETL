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
            LEFT JOIN ASSIGNMENT a ON a.PER_ID = p.PER_ID";

        using IDbConnection connection = new SqlConnection(_connectionString);
        return connection.Query<PersonnelRecord>(query).ToList();
    }
}

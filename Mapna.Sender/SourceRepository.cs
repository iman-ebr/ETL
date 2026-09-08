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
            FROM PERSONEL";

        using IDbConnection connection = new SqlConnection(_connectionString);
        return connection.Query<PersonnelRecord>(query).ToList();
    }
}
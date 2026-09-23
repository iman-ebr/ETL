using Dapper;
using Mapna.Contracts;
using Microsoft.Data.SqlClient;
using Polly;
using System.Data;

namespace Mapna.Sender;

public class SourceRepository
{
    private readonly string _connectionString;
    private const int CircuitBreakThreshold = 5;
    private static readonly TimeSpan CircuitBreakDuration = TimeSpan.FromSeconds(30);

    private readonly IAsyncPolicy _resiliencePolicy;


    public SourceRepository(string connectionString)
    {
        _connectionString = connectionString;
        _resiliencePolicy = BuildResiliencePolicy();
    }

    private IAsyncPolicy? BuildResiliencePolicy()
    {
        var retry = Policy
           .Handle<SqlException>()
           .Or<TimeoutException>()
           .WaitAndRetryAsync(retryCount: 3, attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)));

        var circuitBreaker = Policy
            .Handle<SqlException>()
            .Or<TimeoutException>()
            .CircuitBreakerAsync(CircuitBreakThreshold, CircuitBreakDuration);

        return Policy.WrapAsync(retry, circuitBreaker);

    }

    public async Task<IReadOnlyList<PersonnelRecord>> GetAllPersonnelAsync() =>
    await _resiliencePolicy.ExecuteAsync(() => GetAllPersonnelCoreAsync());

    public async Task<IReadOnlyList<PersonnelRecord>> GetAllPersonnelCoreAsync()
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
        var records = (await connection.QueryAsync<PersonnelRecord>(query)).ToList();

        foreach (var record in records)
        {
            record.Phone = NormalizeNull(record.Phone);
            record.PerAddr = NormalizeNull(record.PerAddr);
            record.PerEmail = NormalizeNull(record.PerEmail);
            record.MobileNo = NormalizeNull(record.MobileNo);
            record.UserPrincipalName = NormalizeNull(record.UserPrincipalName);
            record.PerContract = NormalizeNull(record.PerContract);
            record.BornDate = NormalizeNull(record.BornDate);
            record.CompanyId = NormalizeNull(record.CompanyId);
            record.PerName = NormalizeNull(record.PerName) ?? string.Empty;
            record.PerSurname = NormalizeNull(record.PerSurname) ?? string.Empty;
            record.PerLName = NormalizeNull(record.PerLName) ?? string.Empty;
            record.PerLSurname = NormalizeNull(record.PerLSurname) ?? string.Empty;
            record.SexCode = NormalizeNull(record.SexCode) ?? string.Empty;
            record.NationalCode = NormalizeNull(record.NationalCode) ?? string.Empty;

        }

        return records;
    }
    private static string? NormalizeNull(string? value) =>
    string.IsNullOrWhiteSpace(value) || value.Trim().Equals("NULL", StringComparison.OrdinalIgnoreCase)
        ? null
        : value;
}
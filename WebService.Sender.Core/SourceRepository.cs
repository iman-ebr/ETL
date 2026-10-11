using Dapper;
using WebService.Contracts;
using Microsoft.Data.SqlClient;
using Polly;

namespace WebService.Sender;

public class SourceRepository
{
    private readonly string _connectionString;
    private readonly bool _normalizeArabicLetters;
    private const int CircuitBreakThreshold = 5;
    private const int QueryTimeoutSeconds = 180;
    private static readonly TimeSpan CircuitBreakDuration = TimeSpan.FromSeconds(30);

    private readonly IAsyncPolicy _resiliencePolicy;

    public SourceRepository(string connectionString, bool normalizeArabicLetters = false)
    {
        _connectionString = connectionString;
        _normalizeArabicLetters = normalizeArabicLetters;
        _resiliencePolicy = BuildResiliencePolicy();
    }

    private static IAsyncPolicy BuildResiliencePolicy()
    {
        var retry = Policy
            .Handle<Exception>(SqlTransientErrors.IsTransient)
            .WaitAndRetryAsync(retryCount: 3, attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)));

        var circuitBreaker = Policy
            .Handle<Exception>(SqlTransientErrors.IsTransient)
            .CircuitBreakerAsync(CircuitBreakThreshold, CircuitBreakDuration);

        return Policy.WrapAsync(retry, circuitBreaker);
    }

    private const string SelectAll = @"
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

    private const int IdChunkSize = 1000;

    public Task<IReadOnlyList<PersonnelRecord>> GetPersonnelByIdsAsync(IReadOnlyCollection<int> perIds, CancellationToken cancellationToken = default) =>
        _resiliencePolicy.ExecuteAsync(ct => GetPersonnelByIdsCoreAsync(perIds, ct), cancellationToken);

    private async Task<IReadOnlyList<PersonnelRecord>> GetPersonnelByIdsCoreAsync(IReadOnlyCollection<int> perIds, CancellationToken cancellationToken)
    {
        var records = new List<PersonnelRecord>(perIds.Count);
        if (perIds.Count == 0) return records;

        await using var connection = new SqlConnection(_connectionString);
        foreach (var chunk in perIds.Distinct().Chunk(IdChunkSize))
        {
            var command = new CommandDefinition(SelectAll + " WHERE PER_ID IN @Ids", new { Ids = chunk },
                commandTimeout: QueryTimeoutSeconds, cancellationToken: cancellationToken);
            records.AddRange(await connection.QueryAsync<PersonnelRecord>(command));
        }

        foreach (var record in records)
            PersonnelNormalizer.Normalize(record, _normalizeArabicLetters);

        return records;
    }

    public Task<IReadOnlyList<PersonnelRecord>> GetAllPersonnelAsync(CancellationToken cancellationToken = default) =>
        _resiliencePolicy.ExecuteAsync(ct => GetAllPersonnelCoreAsync(ct), cancellationToken);

    private async Task<IReadOnlyList<PersonnelRecord>> GetAllPersonnelCoreAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        var command = new CommandDefinition(SelectAll, commandTimeout: QueryTimeoutSeconds, cancellationToken: cancellationToken);
        var records = (await connection.QueryAsync<PersonnelRecord>(command)).ToList();

        foreach (var record in records)
            PersonnelNormalizer.Normalize(record, _normalizeArabicLetters);

        return records;
    }
}
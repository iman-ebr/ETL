namespace Mapna.LogData;

public class Personnel
{
    public int Id { get; set; }
    public int PerId { get; set; }
    public string PerName { get; set; } = string.Empty;
    public string PerSurname { get; set; } = string.Empty;
    public int PerStatus { get; set; }
    public string SexCode { get; set; } = string.Empty;
    public string? PerEmail { get; set; }
    public string? MobileNo { get; set; }
    public string? Phone { get; set; }
    public string? PerAddr { get; set; }
    public string PerLName { get; set; } = string.Empty;
    public string PerLSurname { get; set; } = string.Empty;
    public string? BornDate { get; set; }
    public string NationalCode { get; set; } = string.Empty;
    public string? UserPrincipalName { get; set; }
    public int? CompanyId { get; set; }
    public string? PerContract { get; set; }
    public DateTime LastUpdatedUtc { get; set; }
}
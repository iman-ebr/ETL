namespace Mapna.LogData;

public static class LogFieldLimit
{
    public const int StatusMaxLength = 30;
    public const int ReasonMaxLength = 500;
    public const int ChangedFieldsMaxLength = 500;

    public static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
            return value;

        return value[..maxLength];
    }
}
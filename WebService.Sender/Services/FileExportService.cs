using System.IO;
using System.Text;
using Microsoft.Win32;

namespace WebService.Sender.Services;

public interface IFileExportService
{
    Task<string?> ExportCsvAsync<T>(string suggestedName, IReadOnlyList<(string Header, Func<T, object?> Value)> columns, IEnumerable<T> rows);
}

public sealed class FileExportService : IFileExportService
{
    public async Task<string?> ExportCsvAsync<T>(string suggestedName, IReadOnlyList<(string Header, Func<T, object?> Value)> columns, IEnumerable<T> rows)
    {
        var dialog = new SaveFileDialog
        {
            FileName = suggestedName,
            DefaultExt = ".csv",
            Filter = "CSV (*.csv)|*.csv",
            AddExtension = true
        };
        if (dialog.ShowDialog() != true)
            return null;

        var snapshot = rows.ToList();
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", columns.Select(c => Escape(c.Header))));
        foreach (var row in snapshot)
            sb.AppendLine(string.Join(",", columns.Select(c => Escape(c.Value(row)?.ToString()))));

        await File.WriteAllTextAsync(dialog.FileName, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        return dialog.FileName;
    }

    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        if (value[0] is '=' or '+' or '-' or '@') value = "'" + value;
        return value.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
    }
}

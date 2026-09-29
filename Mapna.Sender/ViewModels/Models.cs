using System.Globalization;
using Mapna.Contracts;
using Mapna.LogData;
using Mapna.Sender.Staging;
using Newtonsoft.Json.Linq;

namespace Mapna.Sender.ViewModels;

public enum ResultFilter
{
    All,
    Sent,
    Duplicate,
    Failed
}

public static class Display
{
    private static readonly CultureInfo Persian = CreatePersianCulture();

    public static string Status(SendStatus status) => status switch
    {
        SendStatus.Sent => "ارسال شد",
        SendStatus.Duplicate => "بدون تغییر",
        SendStatus.ValidationFailed => "نامعتبر",
        SendStatus.SendFailed => "ارسال ناموفق",
        _ => "نامشخص"
    };

    public static string RunStatusText(RunStatus status) => status switch
    {
        RunStatus.Running => "در حال اجرا",
        RunStatus.Paused => "متوقف (قابل ادامه)",
        RunStatus.Completed => "تکمیل شد",
        RunStatus.CompletedWithFailures => "تکمیل با خطا",
        RunStatus.Cancelled => "لغو شد",
        RunStatus.Crashed => "خطای راه‌اندازی",
        _ => status.ToString()
    };

    /// <summary>Solar Hijri (Jalali) date/time. The fa-IR culture uses PersianCalendar in .NET.</summary>
    public static string Date(DateTime utc) => utc.ToLocalTime().ToString("yyyy/MM/dd  HH:mm", Persian);

    public static string Time(DateTime local) => local.ToString("HH:mm:ss", CultureInfo.InvariantCulture);

    public static string Duration(TimeSpan span) =>
        span.TotalHours >= 1 ? $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}" : $"{span.Minutes:00}:{span.Seconds:00}";

    public static readonly IReadOnlyDictionary<string, string> FieldLabels = new Dictionary<string, string>
    {
        [nameof(PersonnelRecord.PerId)] = "شناسه پرسنلی",
        [nameof(PersonnelRecord.PerName)] = "نام",
        [nameof(PersonnelRecord.PerSurname)] = "نام خانوادگی",
        [nameof(PersonnelRecord.PerLName)] = "نام (لاتین)",
        [nameof(PersonnelRecord.PerLSurname)] = "نام خانوادگی (لاتین)",
        [nameof(PersonnelRecord.NationalCode)] = "کد ملی",
        [nameof(PersonnelRecord.PerStatus)] = "وضعیت پرسنلی",
        [nameof(PersonnelRecord.SexCode)] = "جنسیت",
        [nameof(PersonnelRecord.BornDate)] = "تاریخ تولد",
        [nameof(PersonnelRecord.MobileNo)] = "موبایل",
        [nameof(PersonnelRecord.Phone)] = "تلفن",
        [nameof(PersonnelRecord.PerEmail)] = "ایمیل",
        [nameof(PersonnelRecord.PerAddr)] = "نشانی",
        [nameof(PersonnelRecord.UserPrincipalName)] = "نام کاربری (UPN)",
        [nameof(PersonnelRecord.CompanyId)] = "شناسه شرکت",
        [nameof(PersonnelRecord.PerContract)] = "نوع قرارداد"
    };

    public static string FieldLabel(string field) => FieldLabels.TryGetValue(field, out var label) ? label : field;

    private static CultureInfo CreatePersianCulture()
    {
        try { return new CultureInfo("fa-IR"); }
        catch (CultureNotFoundException) { return CultureInfo.InvariantCulture; }
    }
}

/// <summary>One row of a sync result grid (live dashboard or history).</summary>
public sealed class RecordResultItem
{
    public int Sequence { get; init; }
    public int PerId { get; init; }
    public string PersonName { get; init; } = string.Empty;
    public SendStatus Status { get; init; }
    public string? Reason { get; init; }
    public string? ChangedFields { get; init; }
    public string? PayloadSnapshot { get; init; }
    public Guid? CorrelationId { get; init; }
    public DateTime TimestampLocal { get; init; } = DateTime.Now;
    public int AttemptCount { get; init; } = 1;

    public string StatusText => Display.Status(Status);

    /// <summary>The audit log stores the receiver's answer machine-readably ("Receiver: Updated"); show it in Persian.</summary>
    public string? ReasonText => Reason switch
    {
        "Receiver: Inserted" => "فرد جدید در مقصد ثبت شد",
        "Receiver: Updated" => "اطلاعات فرد در مقصد به‌روزرسانی شد",
        "Receiver: Duplicate" => "مقصد از قبل به‌روز بود",
        _ => Reason
    };
    public string TimeText => Display.Time(TimestampLocal);
    public bool IsFailure => Status is SendStatus.ValidationFailed or SendStatus.SendFailed;

    public IReadOnlyList<string> ChangedFieldLabels =>
        string.IsNullOrWhiteSpace(ChangedFields)
            ? []
            : ChangedFields.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(Display.FieldLabel).ToList();

    public string ChangedFieldsText => string.Join("، ", ChangedFieldLabels);

    /// <summary>Precomputed once so filtering 100k rows on each keystroke doesn't allocate.</summary>
    public string SearchKey { get; private init; } = string.Empty;

    public bool Matches(ResultFilter filter) => filter switch
    {
        ResultFilter.Sent => Status == SendStatus.Sent,
        ResultFilter.Duplicate => Status == SendStatus.Duplicate,
        ResultFilter.Failed => IsFailure,
        _ => true
    };

    public static RecordResultItem FromProgress(SyncProgress p, int sequence) => Create(new RecordResultItem
    {
        Sequence = sequence,
        PerId = p.CurrentPerId,
        PersonName = p.CurrentPerson,
        Status = p.LastStatus ?? SendStatus.SendFailed,
        Reason = p.LastReason,
        ChangedFields = p.LastChangedFields,
        PayloadSnapshot = p.LastPayloadSnapshot,
        CorrelationId = p.LastCorrelationId
    });

    public static RecordResultItem FromStaging(StagingItem s, int sequence) => Create(new RecordResultItem
    {
        Sequence = sequence,
        PerId = s.PerId,
        PersonName = s.PersonName,
        Status = StagingStatusMapper.ToSendStatus(s.Status),
        Reason = s.Reason,
        ChangedFields = s.ChangedFields,
        PayloadSnapshot = s.PayloadSnapshot,
        TimestampLocal = s.UpdatedAtUtc.ToLocalTime(),
        AttemptCount = s.AttemptCount
    });

    private static RecordResultItem Create(RecordResultItem item) => new()
    {
        Sequence = item.Sequence,
        PerId = item.PerId,
        PersonName = item.PersonName,
        Status = item.Status,
        Reason = item.Reason,
        ChangedFields = item.ChangedFields,
        PayloadSnapshot = item.PayloadSnapshot,
        CorrelationId = item.CorrelationId,
        TimestampLocal = item.TimestampLocal,
        AttemptCount = item.AttemptCount,
        SearchKey = $"{item.PerId} {item.PersonName} {item.Reason}".ToLowerInvariant()
    };
}

public sealed record FieldRow(string Name, string Label, string Value, bool IsChanged)
{
    public bool IsEmpty => Value == "—";
}

/// <summary>Content of the record detail panel: every field of the payload, with the changed ones highlighted.</summary>
public sealed class RecordDetailViewModel
{
    public required RecordResultItem Item { get; init; }
    public required IReadOnlyList<FieldRow> Fields { get; init; }
    public string? PrettyJson { get; init; }
    public bool HasReason => !string.IsNullOrWhiteSpace(Item.Reason);
    public bool HasChanges => Item.ChangedFieldLabels.Count > 0;
    public bool HasFields => Fields.Count > 0;
    public Wpf.Ui.Controls.InfoBarSeverity ReasonSeverity =>
        Item.IsFailure ? Wpf.Ui.Controls.InfoBarSeverity.Error : Wpf.Ui.Controls.InfoBarSeverity.Informational;
    public string ReasonTitle => Item.IsFailure ? "دلیل خطا" : "پاسخ سرور";

    public static RecordDetailViewModel From(RecordResultItem item)
    {
        var changed = new HashSet<string>(
            (item.ChangedFields ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        var fields = new List<FieldRow>();
        string? pretty = null;
        if (!string.IsNullOrWhiteSpace(item.PayloadSnapshot))
        {
            try
            {
                var json = JObject.Parse(item.PayloadSnapshot);
                pretty = json.ToString(Newtonsoft.Json.Formatting.Indented);
                foreach (var name in Display.FieldLabels.Keys)
                {
                    var token = json[name];
                    var value = token is null || token.Type == JTokenType.Null || string.IsNullOrWhiteSpace(token.ToString()) ? "—" : token.ToString();
                    fields.Add(new FieldRow(name, Display.FieldLabel(name), value, changed.Contains(name)));
                }
            }
            catch (Newtonsoft.Json.JsonException)
            {
                pretty = item.PayloadSnapshot;
            }
        }

        return new RecordDetailViewModel { Item = item, Fields = fields, PrettyJson = pretty };
    }

    public static IReadOnlyList<FieldRow> FromRecord(PersonnelRecord record) =>
        Display.FieldLabels.Keys.Select(name =>
        {
            var raw = typeof(PersonnelRecord).GetProperty(name)?.GetValue(record)?.ToString();
            return new FieldRow(name, Display.FieldLabel(name), string.IsNullOrWhiteSpace(raw) ? "—" : raw, false);
        }).ToList();
}

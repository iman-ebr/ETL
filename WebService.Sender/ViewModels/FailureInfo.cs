using WebService.Sender.Staging;

namespace WebService.Sender.ViewModels;

public enum FailureCategory
{
    NotSent,
    InvalidData,
    DuplicateInSource,
    NationalCodeConflict,
    RejectedByServer,
    Network,
    RemovedFromSource,
    Other
}

/// <summary>
/// Turns the stored machine-ish reason into a category + a message a user can act on.
/// The stored Reason is kept untouched (it is the audit trail); this is display-only.
/// </summary>
public static class FailureInfo
{
    public static FailureCategory Classify(StagingItemStatus status, string? reason)
    {
        if (status == StagingItemStatus.Pending) return FailureCategory.NotSent;
        var r = reason ?? string.Empty;

        if (status == StagingItemStatus.ValidationFailed)
            return r.Contains("تکرار شده", StringComparison.Ordinal) ? FailureCategory.DuplicateInSource : FailureCategory.InvalidData;

        if (r.StartsWith("409", StringComparison.Ordinal)) return FailureCategory.NationalCodeConflict;
        if (r.Contains("حذف شده", StringComparison.Ordinal)) return FailureCategory.RemovedFromSource;
        if (r.Contains("دور تلاش", StringComparison.Ordinal) || r.Contains("شبکه", StringComparison.Ordinal) || r.Contains("مهلت", StringComparison.Ordinal))
            return FailureCategory.Network;
        if (r.StartsWith("422", StringComparison.Ordinal) || r.StartsWith("Api responded with", StringComparison.Ordinal))
            return FailureCategory.RejectedByServer;
        return FailureCategory.Other;
    }

    public static string CategoryText(FailureCategory c) => c switch
    {
        FailureCategory.NotSent => "ارسال نشده",
        FailureCategory.InvalidData => "داده‌ی نامعتبر",
        FailureCategory.DuplicateInSource => "PerId تکراری در مبدأ",
        FailureCategory.NationalCodeConflict => "تعارض کد ملی",
        FailureCategory.RejectedByServer => "رد شده توسط سرور",
        FailureCategory.Network => "خطای شبکه / سرور",
        FailureCategory.RemovedFromSource => "حذف‌شده از مبدأ",
        _ => "سایر"
    };

    /// <summary>What the operator should do. Retrying a record whose cause is not fixed only fails again.</summary>
    public static string Advice(FailureCategory c) => c switch
    {
        FailureCategory.NotSent => "اجرا پیش از رسیدن به این رکورد متوقف شد؛ ارسال مجدد آن را می‌فرستد.",
        FailureCategory.InvalidData => "ابتدا داده را در جدول مبدأ اصلاح کنید؛ سپس ارسال مجدد.",
        FailureCategory.DuplicateInSource => "PerId در مبدأ تکراری است؛ ابتدا ردیف‌های تکراری مبدأ را اصلاح کنید.",
        FailureCategory.NationalCodeConflict => "کد ملی متعلق به شخص دیگری در مقصد است؛ داده‌ی مبدأ یا مقصد را بررسی کنید.",
        FailureCategory.RejectedByServer => "سرور رکورد را نپذیرفت؛ پاسخ سرور در ستون دلیل آمده است.",
        FailureCategory.Network => "مشکل موقت ارتباط؛ ارسال مجدد معمولاً مشکل را حل می‌کند.",
        FailureCategory.RemovedFromSource => "رکورد دیگر در مبدأ وجود ندارد؛ ارسال مجدد نتیجه‌ای ندارد.",
        _ => "دلیل را بررسی کنید."
    };

    public static string Message(StagingItemStatus status, string? reason)
    {
        var category = Classify(status, reason);
        if (category == FailureCategory.NotSent) return Advice(category);

        if (string.IsNullOrWhiteSpace(reason)) return "دلیلی ثبت نشده است.";

        // "Api responded with 400: {...}" -> readable Persian prefix, raw body kept after it.
        const string prefix = "Api responded with ";
        if (reason.StartsWith(prefix, StringComparison.Ordinal))
        {
            var rest = reason[prefix.Length..];
            var sep = rest.IndexOf(':');
            if (sep > 0)
                return $"سرور با کد {rest[..sep].Trim()} رکورد را رد کرد: {rest[(sep + 1)..].Trim()}";
        }
        return reason;
    }
}
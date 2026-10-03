using System.Net;
using System.Text;
using Mapna.Contracts;
using Mapna.LogData;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Polly.CircuitBreaker;

namespace Mapna.Sender;

public sealed record SendOutcome(
    SendStatus Status,
    string? Reason,
    string? ChangedFields,
    string PayloadSnapshot,
    bool ConfirmedByReceiver = false,
    Guid? CorrelationId = null);

public sealed class TransientSendException(string message, Exception? inner = null) : Exception(message, inner);

public sealed class ReceiverConfigurationException(string message) : Exception(message);

public class RecordSender
{
    public const string CorrelationHeader = "X-Correlation-Id";
    private const int MaxBodyInReason = 300;

    private readonly HttpClient _httpClient;

    public RecordSender(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<SendOutcome> SendAsync(PersonnelRecord record, SendDecision decision, CancellationToken cancellationToken)
    {
        if (decision.Action == SendAction.SkipValidationFailed)
            return new SendOutcome(SendStatus.ValidationFailed, decision.Reason, null, decision.PayloadSnapshot);

        if (decision.Action == SendAction.SkipDuplicate)
            return new SendOutcome(SendStatus.Duplicate, null, null, decision.PayloadSnapshot);

        var correlationId = Guid.NewGuid();
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/personnel")
        {
            Content = new StringContent(JsonConvert.SerializeObject(record), Encoding.UTF8, "application/json")
        };
        request.Headers.Add(CorrelationHeader, correlationId.ToString());

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (BrokenCircuitException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or Polly.Timeout.TimeoutRejectedException)
        {
            throw new TransientSendException($"خطای شبکه/مهلت زمانی پس از تلاش‌های مجدد: {ex.Message}", ex);
        }

        using (response)
        {
            var code = (int)response.StatusCode;
            if (response.IsSuccessStatusCode)
            {
                var receiverStatus = await TryReadReceiverStatusAsync(response, cancellationToken);

                if (receiverStatus is not ("Inserted" or "Updated" or "Duplicate"))
                    throw new TransientSendException($"پاسخ 2xx بدون تأیید معتبر Receiver (status={receiverStatus ?? "null"}).");


                return new SendOutcome(SendStatus.Sent, receiverStatus is null ? null : $"Receiver: {receiverStatus}",
                    decision.ChangedFields, decision.PayloadSnapshot, ConfirmedByReceiver: true, CorrelationId: correlationId);
            }

            var body = await SafeReadBodyAsync(response, cancellationToken);
            var reason = $"Api responded with {code}: {body}";

            switch (response.StatusCode)
            {
                case HttpStatusCode.Unauthorized:
                case HttpStatusCode.Forbidden:
                    throw new ReceiverConfigurationException($"سرور دریافت‌کننده کلید API را نپذیرفت ({code}). تنظیمات ReceiverApi:ApiKey را بررسی کنید.");
                case HttpStatusCode.NotFound:
                case HttpStatusCode.MethodNotAllowed:
                    throw new ReceiverConfigurationException($"آدرس سرویس دریافت‌کننده پیدا نشد ({code}). تنظیمات ReceiverApi:BaseUrl را بررسی کنید.");
                case HttpStatusCode.TooManyRequests:
                case HttpStatusCode.RequestTimeout:
                    throw new TransientSendException(reason);
            }

            if (code >= 500)
                throw new TransientSendException(reason);

            return new SendOutcome(SendStatus.SendFailed, DescribeRejection(code, body) ?? reason, null, decision.PayloadSnapshot, CorrelationId: correlationId);
        }
    }

    private static string? DescribeRejection(int code, string body)
    {
        try
        {
            var json = JObject.Parse(body);
            return (code, json["status"]?.ToString()) switch
            {
                (409, "RejectedNationalCodeConflict") =>
                    $"409 — کد ملی این فرد در مقصد متعلق به PerId {json["conflictingPerId"]} است؛ برای جلوگیری از ثبت تکراری یک شخص، پذیرفته نشد.",
                (422, _) => "422 — سرور دریافت‌کننده رکورد را در اعتبارسنجی رد کرد.",
                _ => null
            };
        }
        catch
        {
            return null;
        }
    }

    private static async Task<string?> TryReadReceiverStatusAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            return JObject.Parse(json)["status"]?.ToString();
        }
        catch
        {
            return null;
        }
    }

    private static async Task<string> SafeReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            // An IIS/proxy HTML error page can be many KB. It used to be written untruncated into a NVARCHAR(1000)
            // TVP column, where it failed on every retry forever.
            return body.Length <= MaxBodyInReason ? body : body[..MaxBodyInReason] + "…";
        }
        catch
        {
            return string.Empty;
        }
    }
}

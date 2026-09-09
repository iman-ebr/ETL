using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Mapna.Receiver.Auth;

public class ApiKeyAuthenticationHandler : AuthenticationHandler<ApiKeyAuthenticationOptions>
{
    private readonly IReadOnlyDictionary<string, string> _apiKeysByClientName;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<ApiKeyAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IConfiguration configuration)
        : base(options, logger, encoder)
    {
        _apiKeysByClientName =
            configuration.GetSection("Security:ApiKeys").Get<Dictionary<string, string>>()
            ?? new Dictionary<string, string>();
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(ApiKeyAuthenticationOptions.HeaderName, out var providedKeyHeader))
        {
            return Task.FromResult(AuthenticateResult.Fail("X-Api-Key Header Not Sent"));
        }

        var providedKey = providedKeyHeader.ToString();
        var matchedClientName = FindMatchingClient(providedKey);

        if (matchedClientName is null)
        {
            Logger.LogWarning(
                "درخواست با API Key نامعتبر از {RemoteIp} به {Path} رد شد.",
                Context.Connection.RemoteIpAddress, Request.Path);
            return Task.FromResult(AuthenticateResult.Fail("کلید API نامعتبر است."));
        }

        var claims = new[] { new Claim(ClaimTypes.Name, matchedClientName) };
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = ApiKeyAuthenticationOptions.DefaultScheme;
        return Response.WriteAsync("Missing or invalid API key.");
    }

    private string? FindMatchingClient(string providedKey)
    {
        foreach (var (clientName, expectedKey) in _apiKeysByClientName)
        {
            if (FixedTimeEquals(expectedKey, providedKey))
                return clientName;
        }

        return null;
    }

    private static bool FixedTimeEquals(string expected, string actual)
    {
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var actualBytes = Encoding.UTF8.GetBytes(actual);

        return expectedBytes.Length == actualBytes.Length && CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }
}
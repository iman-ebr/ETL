using Microsoft.AspNetCore.Authentication;

namespace WebService.Receiver.Auth;

public class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions
{
    public const string DefaultScheme = "ApiKey";
    public const string HeaderName = "X-Api-Key";
}
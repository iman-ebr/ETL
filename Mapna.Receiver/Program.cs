using Mapna.LogData;
using Mapna.Receiver;
using Mapna.Receiver.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition(ApiKeyAuthenticationOptions.DefaultScheme, new OpenApiSecurityScheme
    {
        Name = ApiKeyAuthenticationOptions.HeaderName,
        Type = SecuritySchemeType.ApiKey,
        In = ParameterLocation.Header,
        Description = "Enter the sender service API key here (X-Api-Key header)"
    });

    options.AddSecurityRequirement(document =>
        new OpenApiSecurityRequirement
        {
            [
                new OpenApiSecuritySchemeReference(
                ApiKeyAuthenticationOptions.DefaultScheme,
                document)
            ] = []
        });
});

builder.Services.AddDbContext<LogDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("AppDatabase")));

builder.Services.AddScoped<PersonnelUpsertService>();

builder.Services
    .AddAuthentication(ApiKeyAuthenticationOptions.DefaultScheme)
    .AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(
        ApiKeyAuthenticationOptions.DefaultScheme, options => { });


builder.Services.AddAuthorization();

var permitLimitPerMinute = builder.Configuration.GetValue<int?>("RateLimiting:PermitLimitPerMinute") ?? 6000;

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = (context, _) =>
    {
        context.HttpContext.Response.Headers["Retry-After"] =
            context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
                ? ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString()
                : "60";
        return ValueTask.CompletedTask;
    };

    options.AddPolicy("PerClientLimit", context =>
    {
        var clientKey = context.User.Identity?.Name ?? context.Connection.RemoteIpAddress?.ToString() ?? "anonymous";

        return RateLimitPartition.GetFixedWindowLimiter(clientKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitLimitPerMinute,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        });
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthentication();

app.UseAuthorization();

app.UseRateLimiter();

app.MapControllers();

app.Run();
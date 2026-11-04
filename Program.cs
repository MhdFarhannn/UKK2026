using System.Text;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Dapper;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using UKK.Controllers;
using UKK.Models;
using UKK.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration
    .AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables()
    .AddCommandLine(args);

DefaultTypeMap.MatchNamesWithUnderscores = true;

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "UKK2026 API",
        Version = "v1",
        Description = "API penyewaan alat UKK2026. Gunakan token dari endpoint login untuk mengakses endpoint yang dilindungi."
    });
    options.AddSecurityDefinition("bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "Masukkan JWT tanpa awalan 'Bearer '."
    });
    options.OperationFilter<SwaggerAuthorizationOperationFilter>();
});
builder.Services
    .AddOptions<RecaptchaOptions>()
    .Bind(builder.Configuration.GetSection("Recaptcha"))
    .Validate(options => !string.IsNullOrWhiteSpace(options.SiteKey), "Recaptcha:SiteKey is required.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.SecretKey), "Recaptcha:SecretKey is required.")
    .Validate(
        options => builder.Environment.IsDevelopment()
            || options.SiteKey != RecaptchaOptions.GoogleTestSiteKey
            && options.SecretKey != RecaptchaOptions.GoogleTestSecretKey,
        "Google reCAPTCHA test keys cannot be used outside Development.")
    .Validate(
        options => builder.Environment.IsDevelopment() || options.AllowedHostnames.Length > 0,
        "Recaptcha:AllowedHostnames must contain at least one production hostname.")
    .Validate(
        options => options.AllowedHostnames.All(hostname =>
            Uri.CheckHostName(hostname) != UriHostNameType.Unknown),
        "Recaptcha:AllowedHostnames contains an invalid hostname.")
    .Validate(
        options => Uri.TryCreate(options.VerificationEndpoint, UriKind.Absolute, out var endpoint)
            && endpoint.Scheme == Uri.UriSchemeHttps,
        "Recaptcha:VerificationEndpoint must be an HTTPS URL.")
    .ValidateOnStart();
builder.Services.AddHttpClient<RecaptchaService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("login", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.Headers.RetryAfter = "60";
        await context.HttpContext.Response.WriteAsJsonAsync(new
        {
            title = "Too Many Requests",
            status = StatusCodes.Status429TooManyRequests,
            detail = "Too many login attempts. Try again in one minute."
        }, cancellationToken);
    };
});
builder.Services.Configure<DendaSettings>(builder.Configuration.GetSection("Denda"));
builder.Services.AddSingleton<Database>();
builder.Services.AddSingleton<JwtService>();
builder.Services.AddSingleton<ImageService>();
builder.Services.AddScoped<LogAktivitasService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<UsersService>();
builder.Services.AddScoped<KategoriService>();
builder.Services.AddScoped<AlatService>();
builder.Services.AddScoped<UnitAlatService>();
builder.Services.AddScoped<StatusBarangServices>();
builder.Services.AddScoped<PeminjamanService>();
builder.Services.AddScoped<PengembalianService>();
builder.Services.AddScoped<LaporanService>();

var jwtSecret = builder.Configuration["Jwt:Secret"]
    ?? throw new InvalidOperationException("Jwt:Secret is not configured.");
var jwtIssuer = builder.Configuration["Jwt:Issuer"]
    ?? throw new InvalidOperationException("Jwt:Issuer is not configured.");
var jwtAudience = builder.Configuration["Jwt:Audience"]
    ?? throw new InvalidOperationException("Jwt:Audience is not configured.");

if (string.IsNullOrWhiteSpace(jwtSecret))
{
    throw new InvalidOperationException("Jwt:Secret is not configured.");
}

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = true,
            ValidAudience = jwtAudience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                try
                {
                    var idClaim = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
                    var roleClaim = context.Principal?.FindFirstValue(ClaimTypes.Role);

                    if (!uint.TryParse(idClaim, out var userId) || roleClaim is null)
                    {
                        context.Fail("Invalid token claims.");
                        return;
                    }

                    var database = context.HttpContext.RequestServices.GetRequiredService<Database>();
                    await using var connection = database.Connect();
                    var currentRole = await connection.QuerySingleOrDefaultAsync<string>("""
                        SELECT r.nama
                        FROM users u
                        INNER JOIN roles r ON r.id = u.id_role
                        WHERE u.id = @Id AND u.deleted_at IS NULL
                        LIMIT 1
                        """, new { Id = userId });

                    if (!string.Equals(currentRole, roleClaim, StringComparison.Ordinal))
                    {
                        context.Fail("The user is deleted or their role has changed.");
                    }
                }
                catch (Exception exception)
                {
                    var logger = context.HttpContext.RequestServices
                        .GetRequiredService<ILoggerFactory>()
                        .CreateLogger("JwtValidation");
                    logger.LogError(exception, "Could not validate the current user against the database");
                    context.HttpContext.Items["JwtValidationDatabaseUnavailable"] = true;
                    context.Fail("The token could not be validated.");
                }
            },
            OnChallenge = async context =>
            {
                if (context.HttpContext.Items.ContainsKey("JwtValidationDatabaseUnavailable"))
                {
                    context.HandleResponse();
                    context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                    await context.Response.WriteAsJsonAsync(new
                    {
                        title = "Service Unavailable",
                        status = StatusCodes.Status503ServiceUnavailable,
                        detail = "The user session could not be validated because the database is unavailable."
                    });
                }
            }
        };
    });
builder.Services.AddAuthorization();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("ConnectionStrings:DefaultConnection is not configured.");
}

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "UKK2026 API v1");
        options.DocumentTitle = "UKK2026 API Documentation";
        options.EnableFilter();
        options.EnablePersistAuthorization();
        options.DisplayRequestDuration();
    });
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapAuth();
app.MapUsers();
app.MapKategori();
app.MapAlat();
app.MapUnitAlat();
app.MapStatusBarang();
app.MapPeminjaman();
app.MapPengembalian();
app.MapLogAktivitas();
app.MapLaporan();

app.Run();

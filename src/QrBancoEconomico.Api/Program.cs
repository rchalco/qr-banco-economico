using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using QrBancoEconomico.Api;
using QrBancoEconomico.Api.Security;
using QrBancoEconomico.Application;
using QrBancoEconomico.Infrastructure;
using Serilog;
using Serilog.Events;
using Serilog.Context;
using System.Diagnostics;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddDotEnvFileIfPresent(builder.Environment.ContentRootPath);
builder.Host.UseSerilog((_, _, loggerConfiguration) => loggerConfiguration
    .MinimumLevel.Information()
    .Enrich.FromLogContext()
    .WriteTo.File("logs/errors-.log", rollingInterval: RollingInterval.Day,
        restrictedToMinimumLevel: LogEventLevel.Error,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}"));
builder.Services.AddControllers();
builder.Services.AddProblemDetails();
// Antes del manejador genérico: un secreto ilegible es un 409 accionable, no un 500 opaco.
builder.Services.AddExceptionHandler<ProtectedSecretExceptionHandler>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.ApiKey,
        In = ParameterLocation.Header,
        Name = "X-Api-Key",
        Description = "Clave del suscriptor: un GUID, por ejemplo 550e8400-e29b-41d4-a716-446655440000. " +
                      "Autentica ante esta API. Se consulta o se carga en la tabla SubscriberApiKeys."
    });
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "Solo para el modo pass-through: JWT que el propio suscriptor obtuvo de Baneco. Se reenvía al banco " +
                      "tal cual y no autentica ante esta API. Si el suscriptor tiene una cuenta concedida en el inventario " +
                      "se ignora: el proxy usa el token de esa cuenta, que renueva por su cuenta."
    });
    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("ApiKey", document, null)] = [],
        [new OpenApiSecuritySchemeReference("Bearer", document, null)] = []
    });
});
builder.Services.Configure<BanecoOptions>(builder.Configuration.GetSection(BanecoOptions.SectionName));
var banecoConnectionString = builder.Configuration.GetConnectionString("BanecoDb");
if (string.IsNullOrWhiteSpace(banecoConnectionString))
{
    throw new InvalidOperationException(
        "Falta configurar ConnectionStrings:BanecoDb. Defínala en .env como ConnectionStrings__BanecoDb o en appsettings.Development.json.");
}

builder.Services.AddDbContext<BanecoDbContext>(options => options.UseSqlServer(banecoConnectionString));
builder.Services.AddHttpContextAccessor();
builder.Services.AddApiKeySecurity(builder.Configuration, builder.Environment);
builder.Services.AddSubscriberRateLimiting();

// La llave maestra se carga al arrancar: sin ella no se pueden descifrar las credenciales del banco,
// y es preferible no levantar a levantar y fallar en la primera operación.
var banecoOptions = builder.Configuration.GetSection(BanecoOptions.SectionName).Get<BanecoOptions>() ?? new BanecoOptions();
var masterKey = SecretProtector.LoadKey(banecoOptions.MasterKey, banecoOptions.MasterKeyFile, builder.Environment.ContentRootPath);
builder.Services.AddSingleton<ISecretProtector>(new SecretProtector(masterKey));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IBanecoTokenService, BanecoTokenService>();
builder.Services.AddScoped<IBanecoTokenProvider, RequestBearerTokenProvider>();

// Cliente propio para autenticar: separarlo del gateway evita que la renovación del token vuelva a
// pasar por el proveedor de token y recurse sobre sí misma.
builder.Services.AddHttpClient(BanecoTokenService.HttpClientName, client =>
{
    client.BaseAddress = new Uri(banecoOptions.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddHttpClient<IBanecoGateway, BanecoGateway>(client =>
{
    client.BaseAddress = new Uri(banecoOptions.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(30);
});
var app = builder.Build();
app.Use(async (context, next) =>
{
    var traceId = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;
    using (LogContext.PushProperty("TraceId", traceId))
    {
        await next();
    }
});
app.UseExceptionHandler();
app.UseSerilogRequestLogging(options => options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
{
    // Trazabilidad: cada solicitud queda atribuida al suscriptor y a la clave concreta que la originó.
    var user = httpContext.User;
    if (user.Identity?.IsAuthenticated != true) return;
    diagnosticContext.Set("SubscriberCode", user.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "(sin código)");
    // Se registra el identificador de la fila, nunca la clave: la clave es el secreto completo.
    diagnosticContext.Set("ApiKeyId", user.FindFirst(ApiKeyAuthenticationDefaults.ApiKeyIdClaimType)?.Value ?? "(sin clave)");
});
app.UseSwagger();
app.UseSwaggerUI();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
app.MapControllers();
await app.EnsureBootstrapApiKeyAsync();
app.Run();

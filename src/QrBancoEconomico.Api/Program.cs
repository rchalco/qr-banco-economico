using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using QrBancoEconomico.Api;
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
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "Ingrese el JWT obtenido en POST /api/baneco/authentication."
    });
    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
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
builder.Services.AddSingleton<IBanecoTokenProvider, RequestBearerTokenProvider>();
builder.Services.AddHttpClient<IBanecoGateway, BanecoGateway>((sp, client) =>
{
    var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<BanecoOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl);
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
app.UseSerilogRequestLogging();
app.UseSwagger();
app.UseSwaggerUI();
app.MapControllers();
app.Run();

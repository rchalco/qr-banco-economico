using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace QrBancoEconomico.Infrastructure;

/// <summary>
/// Contexto para las herramientas de <c>dotnet ef</c>. Existe para que generar y aplicar migraciones no
/// dependa de arrancar la API: el diseño del esquema no necesita la llave maestra ni las credenciales
/// del banco. La cadena de conexión llega por <c>ConnectionStrings__BanecoDb</c>; el valor por omisión
/// solo sirve para que el diseñador infiera el proveedor cuando se generan migraciones sin base activa.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<BanecoDbContext>
{
    private const string FallbackConnectionString =
        "Server=localhost;Database=qr-banco-economico;Trusted_Connection=False;TrustServerCertificate=True";

    public BanecoDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__BanecoDb");
        var builder = new DbContextOptionsBuilder<BanecoDbContext>()
            .UseSqlServer(string.IsNullOrWhiteSpace(connectionString) ? FallbackConnectionString : connectionString);
        return new BanecoDbContext(builder.Options);
    }
}

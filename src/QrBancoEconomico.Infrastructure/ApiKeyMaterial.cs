namespace QrBancoEconomico.Infrastructure;

public sealed class ApiKeySecurityOptions
{
    public const string SectionName = "ApiKeys";

    /// <summary>
    /// Ambiente de este despliegue (dev/stg/prd). Se compara con la columna <c>Environment</c> de cada
    /// clave: una fila emitida para dev es rechazada por una instancia prd aunque compartan la base.
    /// Si queda vacía se deriva del entorno de hosting.
    /// </summary>
    public string Environment { get; set; } = string.Empty;

    /// <summary>Nombre de la cabecera que transporta la clave.</summary>
    public string HeaderName { get; set; } = "X-Api-Key";

    /// <summary>Vigencia de la caché de validación. Acota el retardo de una revocación en despliegues multi-instancia.</summary>
    public int CacheSeconds { get; set; } = 60;

    /// <summary>Frecuencia mínima con la que se actualiza LastUsedAt, para no escribir en cada solicitud.</summary>
    public int UsageStampMinutes { get; set; } = 5;

    /// <summary>Límite por minuto aplicado al tráfico sin credencial válida (health y rechazos).</summary>
    public int AnonymousRequestsPerMinute { get; set; } = 60;

    /// <summary>Si es true y no existe ninguna clave activa, se emite una clave `admin` al iniciar y se imprime una sola vez.</summary>
    public bool BootstrapAdminKey { get; set; }
}

/// <summary>
/// Formato de clave: un GUID, tal cual, en la cabecera <c>X-Api-Key</c>.
/// <code>X-Api-Key: 550e8400-e29b-41d4-a716-446655440000</code>
/// Se guarda en claro para poder cargarla con un <c>INSERT</c>; ver <see cref="Domain.SubscriberApiKey"/>
/// para el costo de seguridad que eso implica.
/// </summary>
/// <remarks>
/// Un GUID v4 aporta 122 bits de aleatoriedad, así que no es adivinable por fuerza bruta y no requiere
/// derivación con costo. Lo que sí importa es el **origen**: <c>Guid.NewGuid()</c> y <c>NEWID()</c> son
/// aleatorios; <c>NEWSEQUENTIALID()</c> no, y una clave generada así se puede predecir a partir de otra.
/// A diferencia del formato anterior, el GUID no lleva el ambiente incrustado: la separación dev/stg/prd
/// la impone la columna <c>Environment</c> de la fila, que se compara con <c>ApiKeys:Environment</c>.
/// </remarks>
public static class ApiKeyMaterial
{
    public static Guid Create() => Guid.NewGuid();

    public static bool TryParse(string? value, out Guid apiKey) =>
        Guid.TryParse(value?.Trim(), out apiKey) && apiKey != Guid.Empty;

    /// <summary>Deriva la etiqueta de ambiente a partir del nombre de entorno de hosting.</summary>
    public static string EnvironmentTag(string environmentName) => environmentName switch
    {
        "Production" => "prd",
        "Staging" => "stg",
        _ => "dev"
    };
}

internal static class ApiKeyCache
{
    internal static string KeyFor(Guid apiKey) => $"apikey:{apiKey}";
    internal static string UsageKeyFor(Guid apiKeyId) => $"apikey-use:{apiKeyId}";
}

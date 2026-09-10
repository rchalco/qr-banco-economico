namespace QrBancoEconomico.Infrastructure;

public sealed class BanecoOptions
{
    public const string SectionName = "Baneco";
    public string BaseUrl { get; init; } = "https://apimkt.baneco.com.bo/apiGateway/";

    /// <summary>Ruta relativa al directorio del proyecto, o absoluta, del archivo con la llave maestra.</summary>
    public string MasterKeyFile { get; init; } = "secrets/baneco-master.key";

    /// <summary>Llave maestra en Base64. Alternativa al archivo para contenedores; tiene prioridad sobre él.</summary>
    public string? MasterKey { get; init; }

    /// <summary>Ruta de autenticación en el API Market de Baneco.</summary>
    public string AuthenticatePath { get; init; } = "api/authentication/authenticate";

    /// <summary>
    /// Vida asumida del token cuando el banco no informa <c>exp</c> ni <c>expiresIn</c>. Baneco lo emite
    /// con 30 minutos; se deja configurable por si el banco cambia el valor.
    /// </summary>
    public int TokenLifetimeMinutes { get; init; } = 30;

    /// <summary>
    /// Margen con el que se renueva antes del vencimiento. Cubre el reloj desfasado entre el proxy y el
    /// banco y la latencia de las solicitudes en vuelo.
    /// </summary>
    public int TokenRenewMarginSeconds { get; init; } = 300;

    /// <summary>
    /// Credencial de respaldo, común a todas las cuentas. Se mantiene por compatibilidad y para el modo
    /// pass-through; con el inventario de cuentas el token se obtiene y renueva solo.
    /// </summary>
    public string? BearerToken { get; init; }

    /// <summary>
    /// Tokens estáticos por cuenta, indexados por el <c>CredentialRef</c> del inventario. Es el mecanismo
    /// anterior: solo se consulta si la cuenta no tiene usuario y contraseña cargados.
    /// </summary>
    public Dictionary<string, BanecoAccountCredentials> Accounts { get; init; } = [];

    public BanecoAccountCredentials? FindCredentials(string? credentialRef) =>
        string.IsNullOrWhiteSpace(credentialRef)
            ? null
            : Accounts.FirstOrDefault(entry => string.Equals(entry.Key, credentialRef, StringComparison.OrdinalIgnoreCase)).Value;
}

/// <summary>Token estático de una cuenta. Vence a los 30 minutos y obliga a intervención manual: preferir credenciales en el inventario.</summary>
public sealed class BanecoAccountCredentials
{
    public string? BearerToken { get; init; }
}

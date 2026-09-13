namespace QrBancoEconomico.Api.Hosting;

/// <summary>
/// Dirección y puerto en los que Kestrel escucha. Se configura en la sección <c>Service</c> de
/// <c>appsettings.json</c> o por variables de entorno (<c>Service__Host</c>, <c>Service__Port</c>).
/// </summary>
public sealed class ServiceEndpointOptions
{
    public const string SectionName = "Service";

    /// <summary>
    /// Interfaz de escucha. Valores admitidos: <c>0.0.0.0</c>, <c>*</c> o <c>::</c> para todas las
    /// interfaces; <c>localhost</c> para solo loopback; o una IP concreta de la máquina
    /// (<c>10.20.30.40</c>, <c>::1</c>) para publicar el servicio en una sola tarjeta de red.
    /// Vacío equivale a todas las interfaces.
    /// </summary>
    public string? Host { get; init; }

    /// <summary>Puerto TCP, entre 1 y 65535.</summary>
    public int? Port { get; init; }
}

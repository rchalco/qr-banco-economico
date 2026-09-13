using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace QrBancoEconomico.Api.Hosting;

public static class ServiceEndpointExtensions
{
    private const int DefaultPort = 5000;

    /// <summary>
    /// Fija la interfaz y el puerto de Kestrel a partir de la sección <c>Service</c>. Devuelve el
    /// extremo resuelto, o <c>null</c> si no hay nada configurado.
    /// </summary>
    public static ServiceEndpoint? ConfigureServiceEndpoint(this WebApplicationBuilder builder)
    {
        var options = builder.Configuration.GetSection(ServiceEndpointOptions.SectionName).Get<ServiceEndpointOptions>()
            ?? new ServiceEndpointOptions();

        // Sin Host ni Port no se toca Kestrel: así siguen mandando ASPNETCORE_URLS, --urls y el
        // applicationUrl de launchSettings.json, que es lo que espera quien depura desde el IDE.
        if (options.Port is null && string.IsNullOrWhiteSpace(options.Host))
        {
            return null;
        }

        // Resolver aquí, y no dentro del callback de Kestrel, hace que una dirección mal escrita
        // falle al arrancar con un mensaje propio y no al primer intento de escucha.
        var endpoint = ServiceEndpoint.Resolve(options.Host, options.Port ?? DefaultPort);
        builder.WebHost.ConfigureKestrel(endpoint.ApplyTo);
        return endpoint;
    }

    /// <summary>
    /// Anuncia en consola dónde quedan los logs de error. Se emite antes del enlace de Kestrel: si el
    /// servicio no llega a arrancar, la ruta ya está en pantalla.
    /// </summary>
    /// <param name="logFilePath">Plantilla de Serilog, del tipo <c>.../logs/errors-.log</c>.</param>
    public static WebApplication LogDiagnosticsLocation(this WebApplication app, string logFilePath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(logFilePath)) ?? app.Environment.ContentRootPath;

        // Crear el directorio ahora hace que la ruta anunciada exista de verdad; el sink de Serilog solo
        // lo crearía al escribir el primer error. No arranca menos si falla: quedarse sin servicio por
        // no poder crear una carpeta de logs es peor que operar sin ese archivo, y el aviso lo deja dicho.
        try
        {
            Directory.CreateDirectory(directory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            app.Logger.LogWarning(exception,
                "No se pudo crear el directorio de logs {LogDirectory}; los errores solo quedarán en consola.",
                directory);
            return app;
        }

        var pattern = Path.GetFileNameWithoutExtension(logFilePath) + "<yyyyMMdd>" + Path.GetExtension(logFilePath);
        app.Logger.LogInformation(
            "Logs de error en {LogDirectory} (un archivo por día: {LogFilePattern}). Hoy: {LogFileToday}",
            directory, pattern, Path.Combine(directory, pattern.Replace("<yyyyMMdd>", DateTime.Now.ToString("yyyyMMdd"))));

        return app;
    }

    /// <summary>
    /// Anuncia en consola la IP y el puerto en los que quedó escuchando el servicio, ya enlazados.
    /// Se lee del servidor y no de la configuración: es lo que de verdad está atendiendo.
    /// </summary>
    public static WebApplication LogListeningAddresses(this WebApplication app)
    {
        app.Lifetime.ApplicationStarted.Register(() =>
        {
            var addresses = app.Services.GetService<IServer>()?.Features.Get<IServerAddressesFeature>()?.Addresses;
            if (addresses is null || addresses.Count == 0)
            {
                app.Logger.LogWarning("El servidor arrancó sin direcciones de escucha declaradas.");
                return;
            }

            foreach (var address in addresses)
            {
                var uri = Uri.TryCreate(address, UriKind.Absolute, out var parsed) ? parsed : null;
                app.Logger.LogInformation(
                    "Servicio escuchando en {Address}  (IP: {Host}, puerto: {Port})",
                    address, uri?.Host ?? "(desconocida)", uri?.Port.ToString() ?? "(desconocido)");
            }
        });

        return app;
    }
}

/// <summary>Interfaz y puerto de escucha ya validados.</summary>
public sealed class ServiceEndpoint
{
    private enum Binding
    {
        /// <summary>Todas las interfaces, IPv4 e IPv6.</summary>
        AnyIp,
        /// <summary>Solo loopback: el servicio no sale de la máquina.</summary>
        Loopback,
        /// <summary>Una dirección concreta de la máquina.</summary>
        Address
    }

    private readonly Binding binding;
    private readonly IPAddress? address;

    private ServiceEndpoint(Binding binding, IPAddress? address, int port)
    {
        this.binding = binding;
        this.address = address;
        Port = port;
    }

    public int Port { get; }

    /// <summary>Dirección tal como se escribe en una URL; IPv6 va entre corchetes.</summary>
    public string Host => binding switch
    {
        Binding.AnyIp => "0.0.0.0",
        Binding.Loopback => "localhost",
        _ => address!.AddressFamily == AddressFamily.InterNetworkV6 ? $"[{address}]" : address!.ToString()
    };

    public string Url => $"http://{Host}:{Port}";

    /// <summary>
    /// Traduce <c>Service:Host</c> y <c>Service:Port</c> a un extremo de escucha. Lanza si el valor no
    /// es utilizable: es preferible no arrancar a arrancar escuchando donde nadie espera al servicio.
    /// </summary>
    public static ServiceEndpoint Resolve(string? host, int port)
    {
        if (port is < 1 or > 65535)
        {
            throw new InvalidOperationException(
                $"Service:Port debe ser un puerto TCP entre 1 y 65535; se recibió '{port}'.");
        }

        var value = host?.Trim() ?? string.Empty;
        // Forma de URL para IPv6: [::1] -> ::1
        if (value.Length >= 2 && value[0] == '[' && value[^1] == ']')
        {
            value = value[1..^1];
        }

        if (value.Length == 0 || value is "*" or "+" or "0.0.0.0" or "::")
        {
            return new ServiceEndpoint(Binding.AnyIp, null, port);
        }

        if (string.Equals(value, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return new ServiceEndpoint(Binding.Loopback, null, port);
        }

        if (IPAddress.TryParse(value, out var parsed))
        {
            return new ServiceEndpoint(Binding.Address, parsed, port);
        }

        throw new InvalidOperationException(
            $"Service:Host no es una dirección de escucha válida: '{host}'. Use una IP de esta máquina " +
            "(por ejemplo 10.20.30.40 o ::1), 'localhost' para escuchar solo en loopback, o '0.0.0.0' " +
            "para todas las interfaces. No se admiten nombres DNS: se escucha sobre una interfaz, no sobre un nombre.");
    }

    internal void ApplyTo(KestrelServerOptions kestrel)
    {
        // Una escucha explícita tiene prioridad sobre ASPNETCORE_URLS, --urls y launchSettings.json.
        switch (binding)
        {
            case Binding.AnyIp:
                kestrel.ListenAnyIP(Port);
                break;
            case Binding.Loopback:
                kestrel.ListenLocalhost(Port);
                break;
            default:
                kestrel.Listen(address!, Port);
                break;
        }
    }

    /// <summary>
    /// Deja en el log dónde va a escuchar el servicio y avisa si la IP fijada no existe en la máquina:
    /// el error nativo de esa situación («Cannot assign requested address») no dice qué revisar.
    /// </summary>
    public void LogTo(ILogger logger)
    {
        var scope = binding switch
        {
            Binding.AnyIp => "todas las interfaces",
            Binding.Loopback => "solo loopback",
            _ => "una interfaz concreta"
        };
        logger.LogInformation("Kestrel escucha en {Url} ({Alcance}), según la sección Service.", Url, scope);

        if (binding == Binding.Address && !IsAssignedToThisHost(address!))
        {
            logger.LogWarning(
                "La dirección {Address} de Service:Host no está asignada a ninguna interfaz de esta máquina. " +
                "Kestrel no podrá enlazarla; revise la configuración o la red del contenedor.", address);
        }
    }

    private static bool IsAssignedToThisHost(IPAddress candidate)
    {
        if (IPAddress.IsLoopback(candidate))
        {
            return true;
        }

        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .SelectMany(nic => nic.GetIPProperties().UnicastAddresses)
                .Any(unicast => unicast.Address.Equals(candidate));
        }
        catch (NetworkInformationException)
        {
            // Sin visibilidad de las interfaces no se puede afirmar nada: no se avisa en falso.
            return true;
        }
    }
}

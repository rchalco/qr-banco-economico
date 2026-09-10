using System.Security.Cryptography;
using System.Text;
using QrBancoEconomico.Application;

namespace QrBancoEconomico.Infrastructure;

/// <summary>
/// Cifra los secretos que se guardan en la base con AES-256-GCM. La llave maestra vive en un archivo
/// fuera del repositorio (<c>Baneco:MasterKeyFile</c>) o en la variable <c>Baneco__MasterKey</c>.
/// </summary>
/// <remarks>
/// Es una medida transitoria: sustituye a la bóveda mientras se habilita una (Key Vault, HSM o
/// SQL Server Always Encrypted). Protege el contenido de un respaldo o de una lectura directa de la
/// base, pero no de quien ya tiene acceso al servidor de aplicación, porque allí conviven llave y datos.
/// No la lleve a producción sin validar el custodio de la llave con seguridad de la información.
/// </remarks>
public sealed class SecretProtector : ISecretProtector
{
    /// <summary>Versión del sobre. Permite rotar el algoritmo o la llave sin romper lo ya cifrado.</summary>
    private const string Version = "v1";
    private const int KeyBytes = 32;
    private const int NonceBytes = 12;
    private const int TagBytes = 16;

    private readonly byte[] _key;

    public SecretProtector(byte[] key)
    {
        if (key.Length != KeyBytes)
            throw new ArgumentException($"La llave maestra debe tener {KeyBytes} bytes (256 bits).", nameof(key));
        _key = key;
    }

    public string Protect(string plaintext)
    {
        ArgumentException.ThrowIfNullOrEmpty(plaintext);

        var nonce = RandomNumberGenerator.GetBytes(NonceBytes);
        var payload = Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = new byte[payload.Length];
        var tag = new byte[TagBytes];

        using var aes = new AesGcm(_key, TagBytes);
        aes.Encrypt(nonce, payload, ciphertext, tag);
        CryptographicOperations.ZeroMemory(payload);

        // nonce || tag || ciphertext: todo lo necesario para descifrar viaja en el mismo sobre.
        var envelope = new byte[NonceBytes + TagBytes + ciphertext.Length];
        nonce.CopyTo(envelope, 0);
        tag.CopyTo(envelope, NonceBytes);
        ciphertext.CopyTo(envelope, NonceBytes + TagBytes);

        return $"{Version}:{Convert.ToBase64String(envelope)}";
    }

    public string Unprotect(string ciphertext)
    {
        ArgumentException.ThrowIfNullOrEmpty(ciphertext);

        var separator = ciphertext.IndexOf(':');
        if (separator <= 0 || !ciphertext.AsSpan(0, separator).SequenceEqual(Version))
            throw new ProtectedSecretException(
                "El valor almacenado no está cifrado con la llave maestra: le falta el sobre 'v1:'. " +
                "Suele ocurrir cuando se cargó con un INSERT en lugar de por la API.");

        byte[] envelope;
        try
        {
            envelope = Convert.FromBase64String(ciphertext[(separator + 1)..]);
        }
        catch (FormatException ex)
        {
            throw new ProtectedSecretException("El secreto almacenado no es Base64 válido.", ex);
        }

        if (envelope.Length < NonceBytes + TagBytes)
            throw new ProtectedSecretException("El secreto almacenado está truncado.");

        var payload = new byte[envelope.Length - NonceBytes - TagBytes];
        using var aes = new AesGcm(_key, TagBytes);
        try
        {
            // Falla si la llave no corresponde o si el dato fue alterado: GCM autentica además de cifrar.
            aes.Decrypt(envelope.AsSpan(0, NonceBytes), envelope.AsSpan(NonceBytes + TagBytes),
                envelope.AsSpan(NonceBytes, TagBytes), payload);
        }
        catch (CryptographicException ex)
        {
            throw new ProtectedSecretException(
                "El secreto no se pudo descifrar: la llave maestra actual no es la que lo cifró, o el valor fue alterado.", ex);
        }

        var result = Encoding.UTF8.GetString(payload);
        CryptographicOperations.ZeroMemory(payload);
        return result;
    }

    /// <summary>Indica si el valor ya está cifrado por este protector, para no cifrar dos veces.</summary>
    public static bool IsProtected(string? value) => value is not null && value.StartsWith($"{Version}:", StringComparison.Ordinal);

    /// <summary>Genera una llave maestra nueva en el formato que espera el archivo.</summary>
    public static string GenerateKeyMaterial() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(KeyBytes));

    /// <summary>
    /// Carga la llave maestra. Prioriza la variable de entorno (útil en contenedores) y recae en el
    /// archivo. Falla al arrancar si no encuentra ninguna: es preferible no levantar a levantar sin
    /// poder descifrar las credenciales del banco.
    /// </summary>
    public static byte[] LoadKey(string? inlineKey, string keyFilePath, string contentRootPath)
    {
        if (!string.IsNullOrWhiteSpace(inlineKey)) return Decode(inlineKey, "la variable Baneco__MasterKey");

        var resolved = Path.IsPathRooted(keyFilePath) ? keyFilePath : Path.Combine(contentRootPath, keyFilePath);
        if (!File.Exists(resolved))
            throw new InvalidOperationException(
                $"No se encontró la llave maestra en '{resolved}'. Genere una con:{Environment.NewLine}" +
                $"  mkdir -p {Path.GetDirectoryName(resolved)} && openssl rand -base64 32 > {resolved} && chmod 600 {resolved}{Environment.NewLine}" +
                "El archivo está en .gitignore y no debe subirse al repositorio. Guarde una copia en la " +
                "bóveda institucional: sin esa llave las credenciales almacenadas son irrecuperables.");

        return Decode(File.ReadAllText(resolved), $"el archivo '{resolved}'");
    }

    private static byte[] Decode(string material, string origin)
    {
        byte[] key;
        try
        {
            key = Convert.FromBase64String(material.Trim());
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException($"La llave maestra en {origin} no es Base64 válido.", ex);
        }

        if (key.Length != KeyBytes)
            throw new InvalidOperationException(
                $"La llave maestra en {origin} tiene {key.Length} bytes; se esperan {KeyBytes} (openssl rand -base64 32).");

        return key;
    }
}

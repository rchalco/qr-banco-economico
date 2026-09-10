using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using QrBancoEconomico.Application;

namespace QrBancoEconomico.Infrastructure;

/// <summary>
/// Cliente de Baneco para las dos operaciones que el proxy expone. Ante un 401 pide al proveedor de
/// token que renueve y reintenta una sola vez: cubre el caso de un token que venció entre que se
/// resolvió y que llegó al banco.
/// </summary>
public sealed class BanecoGateway(HttpClient http, IBanecoTokenProvider tokenProvider) : IBanecoGateway
{
    private static readonly JsonSerializerOptions BanecoJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new BanecoDateOnlyJsonConverter() }
    };

    public async Task<GenerateQrResponse> GenerateQrAsync(BanecoGenerateQrRequest request, CancellationToken ct)
    {
        var result = await SendAsync<GenerateQrResponse>(HttpMethod.Post, "api/qrsimple/generateQR", request, ct);
        return result ?? new(500, "Respuesta vacía del banco", null, null);
    }

    public async Task<IReadOnlyList<PaymentQr>> GetPaidQrsAsync(DateOnly date, CancellationToken ct)
    {
        var result = await SendAsync<PaidQrEnvelope>(HttpMethod.Get, $"api/qrsimple/v2/paidQR/{date:yyyyMMdd}", null, ct);
        return result?.PaymentList ?? [];
    }

    private async Task<T?> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        var response = await SendOnceAsync(method, path, body, ct);
        try
        {
            if (response.StatusCode is HttpStatusCode.Unauthorized && await tokenProvider.TryRenewAsync(ct))
            {
                // El contenido de una solicitud ya enviada no puede reutilizarse: se arma otra desde cero.
                response.Dispose();
                response = await SendOnceAsync(method, path, body, ct);
            }

            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<T>(BanecoJsonOptions, ct);
        }
        finally
        {
            response.Dispose();
        }
    }

    private async Task<HttpResponseMessage> SendOnceAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        // El orden de precedencia (token de la cuenta, llamante, respaldo global) lo decide el proveedor.
        var token = await tokenProvider.GetBearerTokenAsync(ct);
        if (!string.IsNullOrWhiteSpace(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
            request.Content = JsonContent.Create(body, options: BanecoJsonOptions);

        return await http.SendAsync(request, ct);
    }

    private sealed record PaidQrEnvelope(IReadOnlyList<PaymentQr> PaymentList, int ResponseCode, string Message);

    private sealed class BanecoDateOnlyJsonConverter : JsonConverter<DateOnly>
    {
        private static readonly string[] SupportedFormats =
        [
            "yyyy-MM-dd",
            "yyyyMMdd",
            "dd/MM/yyyy",
            "dd-MM-yyyy",
            "MM/dd/yyyy",
            "MM-dd-yyyy"
        ];

        public override DateOnly Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var value = reader.TokenType switch
            {
                JsonTokenType.String => reader.GetString(),
                JsonTokenType.Number => reader.GetInt32().ToString(CultureInfo.InvariantCulture),
                _ => throw new JsonException("paymentDate debe ser una fecha en formato texto o numérico.")
            };

            if (value is not null && DateOnly.TryParseExact(value, SupportedFormats, CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces, out var date))
                return date;

            if (value is not null && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces, out var dateTime))
                return DateOnly.FromDateTime(dateTime.DateTime);

            throw new JsonException($"paymentDate tiene un formato no compatible: '{value}'.");
        }

        public override void Write(Utf8JsonWriter writer, DateOnly value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    }
}

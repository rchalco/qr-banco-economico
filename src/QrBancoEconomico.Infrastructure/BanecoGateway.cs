using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using QrBancoEconomico.Application;

namespace QrBancoEconomico.Infrastructure;

public sealed class BanecoOptions
{
    public const string SectionName = "Baneco";
    public string BaseUrl { get; init; } = "https://apimkt.baneco.com.bo/apiGateway/";
    public string? BearerToken { get; init; }
}

public sealed class BanecoGateway(HttpClient http, IOptions<BanecoOptions> options, IBanecoTokenProvider tokenProvider) : IBanecoGateway
{
    private static readonly JsonSerializerOptions BanecoJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new BanecoDateOnlyJsonConverter() }
    };

    private readonly BanecoOptions _options = options.Value;
    private readonly IBanecoTokenProvider _tokenProvider = tokenProvider;

    public async Task<string> EncryptAsync(string text, string aesKey, CancellationToken ct) =>
        await GetTextAsync($"api/authentication/encrypt?text={Uri.EscapeDataString(text)}&aesKey={Uri.EscapeDataString(aesKey)}", ct);
    public async Task<string> DecryptAsync(string text, string aesKey, CancellationToken ct) =>
        await GetTextAsync($"api/authentication/decrypt?text={Uri.EscapeDataString(text)}&aesKey={Uri.EscapeDataString(aesKey)}", ct);
    public async Task<object> AuthenticateAsync(string userName, string password, CancellationToken ct) =>
        await SendAsync<object>(HttpMethod.Post, "api/authentication/authenticate", new { userName, password }, ct) ?? new { responseCode = 500, message = "Respuesta vacía del banco" };

    public async Task<GenerateQrResponse> GenerateQrAsync(GenerateQrRequest request, CancellationToken ct)
    {
        var result = await SendAsync<GenerateQrResponse>(HttpMethod.Post, "api/qrsimple/generateQR", request, ct);
        return result ?? new(500, "Respuesta vacía del banco", null, null);
    }
    public async Task<ApiResult> CancelQrAsync(string qrId, CancellationToken ct) =>
        await SendAsync<ApiResult>(HttpMethod.Delete, "api/qrsimple/cancelQR", new { qrId }, ct) ?? new(500, "Respuesta vacía del banco");
    public async Task<QrStatusResponse> GetQrStatusAsync(string qrId, CancellationToken ct) =>
        await SendAsync<QrStatusResponse>(HttpMethod.Get, $"api/qrsimple/v2/statusQR/{Uri.EscapeDataString(qrId)}", null, ct) ?? new(500, "Respuesta vacía del banco", 0, []);
    public async Task<IReadOnlyList<PaymentQr>> GetPaidQrsAsync(DateOnly date, CancellationToken ct)
    {
        var result = await SendAsync<PaidQrEnvelope>(HttpMethod.Get, $"api/qrsimple/v2/paidQR/{date:yyyyMMdd}", null, ct);
        return result?.PaymentList ?? [];
    }
    public async Task<object> GetAccountHistoryAsync(AccountHistoryRequest request, CancellationToken ct) =>
        await SendAsync<object>(HttpMethod.Post, "api/accounts/history", request, ct) ?? new { responseCode = 500, message = "Respuesta vacía del banco" };
    public async Task<BatchUploadResponse> UploadBatchAsync(BatchUploadRequest request, CancellationToken ct)
    {
        return await SendAsync<BatchUploadResponse>(HttpMethod.Post, "api/batchPayment/upload", request, ct) ?? new(500, "Respuesta vacía del banco", null);
    }
    private async Task<T?> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var request = CreateRequest(method, path);
        if (body is not null) request.Content = JsonContent.Create(body);
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(BanecoJsonOptions, ct);
    }
    private async Task<string> GetTextAsync(string path, CancellationToken ct)
    {
        using var request = CreateRequest(HttpMethod.Get, path);
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }
    private HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path);
        var token = _tokenProvider.GetBearerToken();
        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        else if (!string.IsNullOrWhiteSpace(_options.BearerToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.BearerToken);
        }
        return request;
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

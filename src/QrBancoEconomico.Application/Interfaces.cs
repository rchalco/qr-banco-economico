namespace QrBancoEconomico.Application;

public interface IBanecoTokenProvider
{
    string? GetBearerToken();
}

public interface IBanecoGateway
{
    Task<string> EncryptAsync(string text, string aesKey, CancellationToken cancellationToken);
    Task<string> DecryptAsync(string text, string aesKey, CancellationToken cancellationToken);
    Task<object> AuthenticateAsync(string userName, string password, CancellationToken cancellationToken);
    Task<GenerateQrResponse> GenerateQrAsync(GenerateQrRequest request, CancellationToken cancellationToken);
    Task<ApiResult> CancelQrAsync(string qrId, CancellationToken cancellationToken);
    Task<QrStatusResponse> GetQrStatusAsync(string qrId, CancellationToken cancellationToken);
    Task<IReadOnlyList<PaymentQr>> GetPaidQrsAsync(DateOnly date, CancellationToken cancellationToken);
    Task<object> GetAccountHistoryAsync(AccountHistoryRequest request, CancellationToken cancellationToken);
    Task<BatchUploadResponse> UploadBatchAsync(BatchUploadRequest request, CancellationToken cancellationToken);
}

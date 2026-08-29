# Code Graph

```mermaid
flowchart LR
  Client --> Api[QrBancoEconomico.Api]
  Bank[Baneco API producción] -->|callbacks| Notify[NotificationsController]
  Api --> BC[BanecoController]
  BC --> App[Application: contratos + IBanecoGateway]
  Notify --> DbContext
  BC --> DbContext[Infrastructure: BanecoDbContext]
  App --> Gateway[Infrastructure: BanecoGateway]
  Gateway --> Bank
  DbContext --> Sql[(SQL Server: qr-banco-economico)]
  Domain[Domain: QrTransaction, QrPayment, BatchUpload] --> DbContext
  App --> Domain
```

## Dependencias

```text
Api -> Application -> Domain
Api -> Infrastructure -> Application, Domain
Infrastructure -> SQL Server / API Market Baneco
```

## Flujos

- QR: `POST /qrs` → Baneco → `QrTransactions`; el callback agrega `QrPayments`.
- Consulta: estado, pagados y movimientos son proxies de lectura hacia Baneco.
- Planilla: `POST /batches` → Baneco → `BatchUploads`.
- Seguridad: token y AES son configuración externa.

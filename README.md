# QR Banco Económico API

## Levantar la API

1. Copie `.env.example` a `.env` y complete sus valores, o expórtelos como variables de entorno. La API carga automáticamente `.env` solo para desarrollo local. Configure `ConnectionStrings__BanecoDb` con SQL Server local y `Baneco__BearerToken` con un token temporal. La API valida esta cadena al arrancar para evitar que una transacción falle más tarde. Nunca guarde claves AES, tokens ni contraseñas en este repositorio. El JWT de Baneco configurado para esta integración tiene una vigencia de 30 minutos; renuévelo antes de expirar.
2. Restaure y ejecute:

```bash
dotnet restore QrBancoEconomico.slnx
dotnet run --project src/QrBancoEconomico.Api
```

La creación y actualización del esquema se realizan mediante las migraciones siguientes.

Para crear una migración cuando cambie el modelo:

```bash
dotnet tool restore
dotnet ef migrations add NombreDescriptivo --project src/QrBancoEconomico.Infrastructure --startup-project src/QrBancoEconomico.Api --output-dir Migrations
dotnet ef database update --project src/QrBancoEconomico.Infrastructure --startup-project src/QrBancoEconomico.Api
```

La base actual fue creada antes de adoptar migraciones. No ejecute `database update` sobre esas tablas existentes: úselo en una base vacía o después de acordar una estrategia de línea base/migración de datos.

## Endpoints

| Método | Ruta | Descripción |
|---|---|---|
| GET | `/health` | Comprueba conectividad con SQL Server. |
| GET | `/api/baneco/encryption/encrypt?text=&aesKey=` | Cifra con la API Baneco. |
| GET | `/api/baneco/encryption/decrypt?text=&aesKey=` | Descifra con la API Baneco. |
| POST | `/api/baneco/authentication` | Solicita token Baneco; body: `userName`, `password` cifrada. |
| POST | `/api/baneco/qrs` | Genera QR; `accountCredit` debe estar cifrada y codificada en Base64. Si `modifyAmount` es `true`, `amount` debe ser `0`; si es `false`, `amount` debe ser mayor que cero. |
| DELETE | `/api/baneco/qrs/{qrId}` | Anula un QR. |
| GET | `/api/baneco/qrs/{qrId}` | Consulta estado y pagos del QR. |
| GET | `/api/baneco/qrs/paid/{yyyy-MM-dd}` | Lista QR pagados de la fecha. |
| POST | `/api/baneco/accounts/history` | Consulta movimientos de cuenta cifrada. |
| POST | `/api/baneco/batches` | Carga planilla. |
| POST | `/api/notifications/qr-payments` | Callback del banco para pago QR. |
| POST | `/api/notifications/batch-status` | Callback del banco para detalle de planilla. |

Todas las solicitudes salientes al API Market de Baneco, incluidos los endpoints de cifrado y autenticación, envían `Authorization: Bearer <Baneco__BearerToken>`.

En Swagger, use **Authorize**, pegue el JWT devuelto por `/api/baneco/authentication` y confirme. Swagger UI añadirá `Authorization: Bearer <JWT>` a las solicitudes siguientes, y la API lo reenviará a Baneco.

Los errores no controlados se registran con Serilog en `logs/errors-AAAAmmdd.log`, junto con la propiedad `TraceId` para correlacionarlos con la solicitud; el directorio está excluido del control de versiones.

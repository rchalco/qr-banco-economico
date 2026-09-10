# QR Banco Económico API

## Levantar la API

1. Copie `.env.example` a `.env` y complete sus valores, o expórtelos como variables de entorno. La API carga automáticamente `.env` solo para desarrollo local. Configure `ConnectionStrings__BanecoDb` con SQL Server local. La API valida esta cadena al arrancar para evitar que una transacción falle más tarde. Nunca guarde claves AES, tokens ni contraseñas en este repositorio.
2. Genere la **llave maestra** que cifra las credenciales de Baneco guardadas en la base. El archivo está en `.gitignore` y no debe subirse:

```bash
mkdir -p src/QrBancoEconomico.Api/secrets
openssl rand -base64 32 > src/QrBancoEconomico.Api/secrets/baneco-master.key
chmod 600 src/QrBancoEconomico.Api/secrets/baneco-master.key
```

   Guarde una copia en la bóveda institucional: **sin esa llave las credenciales almacenadas son irrecuperables** y hay que volver a cargarlas. La API no arranca si no la encuentra, y el mensaje de error indica cómo generarla. En contenedores puede pasarla por `Baneco__MasterKey` (Base64) en lugar del archivo.

3. Restaure y ejecute:

```bash
dotnet restore QrBancoEconomico.slnx
dotnet run --project src/QrBancoEconomico.Api
```

La creación y actualización del esquema se realizan mediante las migraciones siguientes.

Para crear una migración cuando cambie el modelo:

```bash
dotnet tool restore
dotnet ef migrations add NombreDescriptivo --project src/QrBancoEconomico.Infrastructure --startup-project src/QrBancoEconomico.Infrastructure --output-dir Migrations
ConnectionStrings__BanecoDb="..." dotnet ef database update --project src/QrBancoEconomico.Infrastructure --startup-project src/QrBancoEconomico.Infrastructure
```

Las herramientas usan `DesignTimeDbContextFactory`, de modo que generar o aplicar migraciones no requiere la llave maestra ni arrancar la API: el esquema no depende de los secretos.

Las migraciones son **idempotentes**: cada objeto se crea solo si falta, con guardas sobre el catálogo
(`OBJECT_ID`, `COL_LENGTH`, `sys.indexes`) y no sobre `__EFMigrationsHistory`. Eso hace que
`database update` converja al esquema objetivo desde cualquier punto de partida —base vacía, base
gestionada por EF, o base creada a mano antes de adoptar migraciones y por tanto sin historial— y que
repetirlo no tenga efecto. Ver `Migrations/IdempotentMigrationBuilder.cs`.

Verificado en los tres escenarios contra SQL Server 2022: la base construida desde cero y la base
legada quedan con los mismos 109 objetos (columnas, índices y claves foráneas), los datos preexistentes
se conservan y la segunda ejecución responde «The database is already up to date».

`Down()` conserva el DDL generado por EF y **no** es idempotente: revertir es un acto deliberado sobre
un estado conocido y, sobre una base creada a mano, `DropTable` destruiría datos que la migración nunca
creó. Revise antes de usar `database update <MigraciónAnterior>`.

## Endpoints

| Método | Ruta | Alcance | Descripción |
|---|---|---|---|
| GET | `/health` | — | Comprueba conectividad con SQL Server. Único endpoint anónimo. |
| POST | `/api/baneco/qrs` | `qr:write` | **Genera un QR.** No lleva `accountCredit`: la cuenta a acreditar es la del suscriptor. Si `modifyAmount` es `true`, `amount` debe ser `0`; si es `false`, `amount` debe ser mayor que cero. |
| GET | `/api/baneco/qrs/paid/{yyyy-MM-dd}` | `qr:read` | **Lista los QR pagados de la fecha**, filtrados a los emitidos por el suscriptor. |
| POST | `/api/notifications/qr-payments` | `notifications:write` | Callback del banco para pago QR. |
| POST | `/api/notifications/batch-status` | `notifications:write` | Callback del banco para detalle de planilla. |
| GET | `/api/admin/subscribers` | `admin` | Lista suscriptores y el estado de sus claves. |
| POST | `/api/admin/subscribers` | `admin` | Da de alta un suscriptor con su cuenta de ahorro. |
| GET | `/api/admin/subscribers/{id}` | `admin` | Consulta un suscriptor. |
| PATCH | `/api/admin/subscribers/{id}` | `admin` | Actualiza nombre, contacto, cuenta de ahorro, estado o cuota. |
| POST | `/api/admin/subscribers/{id}/keys` | `admin` | Emite una API Key. La clave en claro solo aparece en esta respuesta. |
| DELETE | `/api/admin/subscribers/{id}/keys/{keyId}` | `admin` | Revoca una API Key. |
| GET | `/api/admin/baneco-accounts` | `admin` | Inventario de cuentas de Baneco y sus suscriptores. |
| POST | `/api/admin/baneco-accounts` | `admin` | Registra una cuenta de Baneco con sus credenciales. |
| GET | `/api/admin/baneco-accounts/{id}` | `admin` | Consulta una cuenta y quién la usa. |
| PATCH | `/api/admin/baneco-accounts/{id}` | `admin` | Actualiza credenciales, códigos o baja de una cuenta. |
| POST | `/api/admin/baneco-accounts/{id}/verify-credentials` | `admin` | Prueba las credenciales contra Baneco. No devuelve el token. |
| GET | `/api/admin/subscribers/{id}/accounts` | `admin` | Cuentas concedidas a un suscriptor. |
| POST | `/api/admin/subscribers/{id}/accounts` | `admin` | Concede una cuenta al suscriptor. |
| DELETE | `/api/admin/subscribers/{id}/accounts/{accountId}` | `admin` | Revoca la concesión. |

**La superficie hacia los suscriptores son dos métodos y nada más:** generar un QR y listar los QR pagados. El resto de la API de Baneco (cifrado/descifrado, autenticación, anulación, estado de QR, movimientos de cuenta y planillas) **no se expone**. En particular, `/encryption/decrypt` obligaba a aceptar la clave AES del banco por *query string*, donde queda registrada en los logs de acceso; y `/authentication` dejó de tener sentido desde que el proxy gestiona el token por su cuenta. Los alcances `crypto:use`, `baneco:auth`, `accounts:read` y `batches:write` siguen siendo emisibles pero ya no habilitan ningún endpoint; restaurar un flujo es volver a agregar su método al controlador y al `IBanecoGateway`.

## Control de acceso por API Key

La API se distribuye a varios suscriptores (canales, aplicaciones o entidades). Cada uno se identifica con una clave propia —**un GUID**— en la cabecera `X-Api-Key`:

```text
X-Api-Key: 550e8400-e29b-41d4-a716-446655440000
```

> **La clave se guarda en claro en `SubscriberApiKeys.ApiKey`**, para poder cargarla con un `INSERT`
> sin pasar por la API. Es una concesión deliberada a la operación con un costo real: quien obtenga una
> copia de esa tabla —un respaldo, un volcado, una inyección SQL en cualquier otro punto— obtiene
> credenciales funcionales de **todos** los suscriptores. El diseño anterior guardaba solo el SHA-256 y
> una filtración no servía de nada. Compénselo restringiendo el acceso a la tabla, cifrando los
> respaldos y rotando ante cualquier sospecha.

- **Cierre por defecto.** Todo endpoint exige credencial válida salvo `/health`. Sin clave la respuesta es `401`; con clave válida pero sin el alcance del recurso, `403`.
- **Alcances.** Cada clave lleva solo los alcances que su suscriptor necesita (ver la columna de la tabla anterior). `admin` no implica los demás y no debe concederse a claves de canal.
- **Almacenamiento.** La clave se guarda en claro en `SubscriberApiKeys.ApiKey`, con índice único. Puede consultarse con un `SELECT` y cargarse con un `INSERT`; ya no se pierde si no se anota la respuesta de la emisión.
- **Origen del GUID.** Genérelo con `NEWID()` o `Guid.NewGuid()`: 122 bits aleatorios, no adivinable. **Nunca con `NEWSEQUENTIALID()`**, que produce valores contiguos y permite predecir una clave a partir de otra.
- **Separación de ambientes.** El GUID no lleva el ambiente incrustado: lo impone la columna `Environment` de la fila, comparada con `ApiKeys__Environment`. Una fila `dev` es rechazada por una instancia `prd` aunque compartan la base.
- **Cuota.** Ventana fija de un minuto por suscriptor (`requestsPerMinute`, 120 por defecto); al excederla se responde `429` con `Retry-After`. El tráfico sin credencial se particiona por IP.
- **Trazabilidad.** Cada solicitud queda registrada con `SubscriberCode` y `ApiKeyId` —el identificador de la fila, que no autentica— junto al `TraceId`. La clave presentada **nunca** se registra: ahora es el secreto completo, y un log con una clave rechazada sería un log con una credencial.
- **Aislamiento.** `QrTransactions` se atribuye al suscriptor que lo originó y `qrs/paid` devuelve únicamente los QR propios, aunque la cuenta de Baneco atienda a varios suscriptores. El `transactionId` es único **dentro de cada suscriptor**, no globalmente.

### Primera clave y alta de suscriptores

Hay tres caminos, y el tercero es el que habilita este diseño:

**a) Bootstrap.** Arranque una única vez con `ApiKeys__BootstrapAdminKey=true`. Si no existe ninguna clave activa, la API emite una con alcance `admin` y la imprime por consola. Vuelva a poner la variable en `false` después. Si queda en `false` y no hay claves, el arranque lo advierte con nivel `Critical` y toda solicitud responde `401`. Ojo: la condición es *no existe ninguna clave activa*, así que si ya tiene una hay que revocarla antes (`UPDATE SubscriberApiKeys SET RevokedAt = SYSDATETIMEOFFSET() WHERE ...`).

**b) `SELECT`.** Como la clave está en claro, se recupera en cualquier momento:

```sql
SELECT s.Code, k.ApiKey, k.Scopes, k.Environment
FROM SubscriberApiKeys k JOIN Subscribers s ON s.Id = k.SubscriberId
WHERE k.RevokedAt IS NULL;
```

**c) `INSERT` a mano**, eligiendo usted el GUID:

```sql
INSERT INTO SubscriberApiKeys (Id, SubscriberId, ApiKey, Environment, Scopes, Label, CreatedAt)
SELECT NEWID(), Id, NEWID(), 'dev', 'qr:write qr:read', 'canal-app-movil', SYSDATETIMEOFFSET()
FROM Subscribers WHERE Code = 'canal-app-movil';
```

`Id` es el identificador interno que va a los logs; `ApiKey` es la credencial. Deben ser valores
distintos. `Scopes` va separado por espacios y `Environment` debe coincidir con `ApiKeys__Environment`.
La caché de validación vive en memoria del proceso, así que una clave recién insertada tarda hasta
`ApiKeys__CacheSeconds` (60 s por defecto) en ser reconocida.

Y por la API:

1. Con una clave `admin` ya disponible, dé de alta al suscriptor y emita su clave:

```bash
curl -X POST http://localhost:5180/api/admin/subscribers \
  -H "X-Api-Key: $ADMIN_KEY" -H 'Content-Type: application/json' \
  -d '{"code":"canal-app-movil","name":"Canal App Móvil","contactEmail":"canal@ejemplo.bo","requestsPerMinute":300}'

curl -X POST http://localhost:5180/api/admin/subscribers/{id}/keys \
  -H "X-Api-Key: $ADMIN_KEY" -H 'Content-Type: application/json' \
  -d '{"scopes":["qr:write","qr:read"],"label":"canal-app-movil-2026-09","expiresAt":"2027-09-06T00:00:00Z"}'
# -> {"key":{...},"apiKey":"550e8400-e29b-41d4-a716-446655440000"}
```

2. Para rotar sin corte: emita la nueva clave, entréguela al suscriptor, confirme su uso en `lastUsedAt` y recién entonces revoque la anterior con `DELETE .../keys/{keyId}`. La revocación y la desactivación de un suscriptor invalidan la caché local de inmediato; en despliegues multi-instancia el resto de las instancias tardan como máximo `ApiKeys__CacheSeconds`.

Los callbacks de Baneco (`/api/notifications/*`) requieren su propia clave con alcance `notifications:write`, emitida a un suscriptor que represente al banco y separada de las claves de canal. Coordine con Baneco el envío de la cabecera `X-Api-Key` **antes** de desplegar: si el banco no puede enviar cabeceras propias, la alternativa es lista blanca de IP o mTLS en el borde, y ese endpoint quedaría fuera de este esquema.

## Cuenta de ahorro por suscripción

**Cada suscripción tiene su propia cuenta de ahorro**, y es la única fuente de `accountCredit`. El proxy
**no la recibe en la solicitud**: la carga el administrador en el alta, cifrada, y la agrega él mismo al
cuerpo que viaja al banco.

```text
El suscriptor envía                      El proxy envía a Baneco
─────────────────────                    ───────────────────────
{                                        {
  "transactionId": "68e5…",                "transactionId": "68e5…",
                                           "accountCredit": "l/a6/iwZ…",  ◀── de Subscribers, descifrada
  "currency": "BOB",                       "currency": "BOB",
  "amount": 5,                             "amount": 5,
  "description": "pago api baneco",        "description": "pago api baneco",
  "dueDate": "2026-08-29",                 "dueDate": "2026-08-29",
  "singleUse": false,                      "singleUse": false,
  "modifyAmount": true,                    "modifyAmount": true,
  "branchCode": "E0001"                    "branchCode": "E0001"
}                                        }
```

- **Enviar `accountCredit` es un `400`, no un campo ignorado.** El contrato declara
  `additionalProperties: false`, de modo que un integrador que siga mandándolo lo sabe de inmediato en
  lugar de creer que se honró mientras el QR acredita otra cuenta.
- **Por qué no puede venir del cuerpo.** Es el criptograma de una cuenta bancaria. Si el proxy lo
  aceptara, cualquier suscriptor con el criptograma de otro podría emitir QR que acreditan a un tercero.
  Al vivir en la fila del suscriptor, la cuenta acreditada queda determinada por la API Key presentada.
- **Doble capa de cifrado.** El valor llega ya cifrado con la clave AES de Baneco; el proxy le agrega su
  propia capa (`v1:…`, AES-256-GCM con la llave maestra) antes de guardarlo. Ni la base ni las respuestas
  de la API exponen el criptograma: `GET /api/admin/subscribers` solo informa `hasSavingsAccount`.
- **Se valida en el alta, no en cada QR.** Un valor que no sea Base64 se rechaza con `400` al cargarlo,
  cuando el administrador todavía está mirando, y no como un rechazo del banco en la primera venta.
- **Sin cuenta de ahorro no se emiten QR:** `POST /api/baneco/qrs` responde `409` indicando que hay que
  cargarla. El suscriptor `bootstrap-admin` no tiene ninguna a propósito: solo administra.
- `QrTransactions.AccountCreditEncrypted` guarda la cuenta acreditada **con la capa de la llave maestra**,
  para que el registro histórico no deje el criptograma a la vista de quien lea la tabla.

```bash
# Alta con cuenta de ahorro
curl -X POST http://localhost:5180/api/admin/subscribers \
  -H "X-Api-Key: $ADMIN_KEY" -H 'Content-Type: application/json' \
  -d '{"code":"canal-app-movil","name":"Canal App Móvil","contactEmail":"canal@ejemplo.bo",
       "savingsAccount":"<cuenta cifrada con la clave AES de Baneco, en Base64>","requestsPerMinute":300}'

# Cambio de cuenta de ahorro (por ejemplo, si el canal cambia de cuenta recaudadora)
curl -X PATCH http://localhost:5180/api/admin/subscribers/{id} \
  -H "X-Api-Key: $ADMIN_KEY" -H 'Content-Type: application/json' \
  -d '{"savingsAccount":"<nueva cuenta cifrada en Base64>"}'
```

## Cuentas de Baneco: inventario N-N

La API Key identifica **al suscriptor ante este proxy**; la credencial de Baneco identifica **la cuenta bancaria contra la que el proxy opera**. Son dos planos independientes:

| | API Key del proxy | Credencial de Baneco |
|---|---|---|
| Cabecera | `X-Api-Key` | `Authorization: Bearer` |
| La emite | este proxy | Baneco |
| Se almacena | hash SHA-256 en `SubscriberApiKeys` | usuario y contraseña **cifrados** en `BanecoAccounts`; el token nunca se persiste |

El proxy se distribuye como multiplexor: mantiene un inventario de cuentas de Baneco y las concede a suscriptores en una relación **N-N**. Un suscriptor puede operar varias cuentas y una cuenta puede atender a varios suscriptores.

```text
canal-app-movil ──┐                          ┌── recaudos-01  (credentialRef: recaudos01)
                  ├── concesiones (N-N) ─────┤
canal-comercios ──┘                          └── recaudos-02  (credentialRef: recaudos02)
```

**Selección de cuenta por solicitud.** Con una sola cuenta concedida se usa esa; con varias, la marcada como predeterminada, o la que indique la cabecera `X-Baneco-Account: {code}`. Si hay varias y ninguna predeterminada, la respuesta es `400`; si pide una que no tiene concedida, `403`; si la cuenta está dada de baja, `409`.

**Qué define cada plano.** La cuenta del inventario define **con qué credencial se habla con el banco**; la cuenta de ahorro del suscriptor define **a qué cuenta se acredita**. Por eso varias suscripciones pueden compartir una credencial de Baneco y aun así cada una recaudar en su propia cuenta.

**Modo pass-through.** Un suscriptor sin cuentas concedidas aporta su propio JWT en `Authorization: Bearer`, y en ese modo la renovación del token corre por su cuenta. Su cuenta de ahorro sigue saliendo del alta: el pass-through afecta solo al plano de la credencial, nunca al de la cuenta acreditada.

> `BanecoAccounts.AccountCodeEncrypted` y `BatchDebitAccountEncrypted` quedaron **sin uso** al mover la cuenta a acreditar al suscriptor y al retirar el flujo de planillas. Las columnas se conservan por los datos existentes y pueden eliminarse en una migración posterior.

## Token de Baneco: se obtiene y se renueva solo

Baneco emite el token con **30 minutos** de vigencia. El proxy guarda el usuario y la contraseña de cada cuenta (cifrados) y se autentica por su cuenta contra `api/authentication/authenticate`; nadie tiene que pegar un token en un archivo de configuración ni reiniciar el servicio cuando vence.

```text
GET/POST del suscriptor
      │
      ▼
 ¿token en caché y vigente?  ── sí ──▶ se usa
      │ no
      ▼
 BanecoAccounts: usuario+contraseña cifrados ──▶ descifra con la llave maestra
      │
      ▼
 POST api/authentication/authenticate ──▶ token ──▶ caché hasta exp − 5 min
      │
      └── si Baneco responde 401: invalida, reautentica y reintenta una vez
```

- **Vencimiento.** Se lee del claim `exp` del JWT. Si el token no es un JWT o no lo trae, se asume `Baneco__TokenLifetimeMinutes` (30).
- **Margen de renovación.** `Baneco__TokenRenewMarginSeconds` (300 s por defecto) cubre el reloj desfasado entre el proxy y el banco y las solicitudes en vuelo.
- **Concurrencia.** Un solo hilo por cuenta autentica; el resto espera y reutiliza el resultado. Sin esto, una ráfaga tras el vencimiento dispararía tantas autenticaciones como solicitudes concurrentes.
- **El token nunca se persiste** ni se registra en los logs. Solo se guarda `credentialsVerifiedAt` para diagnóstico.
- **Caché por proceso.** Con varias instancias cada una autentica por separado. Queda **pendiente confirmar con Baneco** si la autenticación es de sesión única (si emitir un token invalida el anterior) y si hay límite de tasa en `/authenticate`: de ser así hay que mover la caché a Redis con lock distribuido.

**Precedencia del token hacia el banco:** token gestionado de la cuenta → `Baneco__Accounts__{credentialRef}__BearerToken` (token estático, mecanismo anterior) → `Authorization: Bearer` del llamante → `Baneco__BearerToken` global. La cuenta del inventario gana sobre la cabecera: un suscriptor no puede sustituir la credencial de la cuenta que se le concedió. Si una cuenta cae al token estático se registra una advertencia, porque ese camino sí vence a los 30 minutos sin renovarse.

### Cifrado de las credenciales en la base

Medida **transitoria** mientras se habilita una bóveda. El usuario y la contraseña se guardan en `BanecoAccounts` cifrados con **AES-256-GCM**; la llave maestra de 256 bits vive en un archivo fuera del repositorio (`Baneco__MasterKeyFile`, por omisión `secrets/baneco-master.key`).

```text
v1:{ base64( nonce(12) ‖ tag(16) ‖ ciphertext ) }
```

- GCM **autentica además de cifrar**: un valor alterado en la base se rechaza al descifrarlo en lugar de producir basura.
- El nonce es aleatorio por operación, de modo que cifrar dos veces el mismo valor da resultados distintos.
- El prefijo `v1` permite rotar algoritmo o llave más adelante sin romper lo ya cifrado.

**Alcance real de esta protección:** cubre un respaldo de la base, una copia del `.bak` o una lectura directa del motor. **No** cubre a quien ya tiene acceso al servidor de aplicación, porque allí conviven la llave y los datos. Antes de producción, valide el custodio de la llave con seguridad de la información y evalúe migrar a Key Vault, HSM o SQL Server Always Encrypted.

### Alta de una cuenta y su concesión

```bash
# 1. Inventario: credenciales y códigos. Se cifran antes de guardarse y no vuelven a salir por la API.
curl -X POST http://localhost:5180/api/admin/baneco-accounts \
  -H "X-Api-Key: $ADMIN_KEY" -H 'Content-Type: application/json' \
  -d '{"code":"recaudos-01","name":"Recaudos 01",
       "authUserName":"<usuario de Baneco>","authPassword":"<contraseña de Baneco>",
       "accountCodeEncrypted":"<cuenta cifrada con la clave AES del banco, en Base64>"}'

# 2. Verificación: prueba las credenciales contra el banco antes de dar el alta por buena.
curl -X POST http://localhost:5180/api/admin/baneco-accounts/{accountId}/verify-credentials \
  -H "X-Api-Key: $ADMIN_KEY"
# -> {"succeeded":true,"message":"Autenticación correcta.","tokenExpiresAt":"..."}

# 3. Concesión al suscriptor
curl -X POST http://localhost:5180/api/admin/subscribers/{subscriberId}/accounts \
  -H "X-Api-Key: $ADMIN_KEY" -H 'Content-Type: application/json' \
  -d '{"accountId":"{accountId}","isDefault":true}'
```

Para **rotar la contraseña** basta un `PATCH` con `authPassword`: el token cacheado se invalida en el acto, de modo que la siguiente solicitud reautentica en lugar de fallar con 401 hasta que venza. Enviar `""` borra la credencial.

`GET /api/admin/baneco-accounts/{id}` muestra qué suscriptores usan la cuenta y `GET /api/admin/subscribers/{id}/accounts` el inventario en el sentido inverso. Ninguna respuesta devuelve criptogramas ni secretos: solo `hasCredentials`, `hasAccountCode` y `credentialsVerifiedAt`.

**Colisión de identificadores en cuenta compartida.** Baneco exige `transactionId` único por cuenta. Si dos suscriptores comparten una cuenta y repiten el mismo identificador, el proxy responde `409` antes de llamar al banco, con índices únicos `(BanecoAccountId, MerchantTransactionId)` y `(BanecoAccountId, BatchId)` que lo respaldan.

En Swagger, use **Authorize**: cargue su `X-Api-Key` (el GUID) y, solo en modo pass-through, además el JWT que el propio suscriptor obtuvo de Baneco.

Los errores no controlados se registran con Serilog en `logs/errors-AAAAmmdd.log`, junto con la propiedad `TraceId` para correlacionarlos con la solicitud; el directorio está excluido del control de versiones.

## Migración a multi-suscriptor

`AddSubscriberApiKeys` agrega `Subscribers` y `SubscriberApiKeys`, y una columna `SubscriberId` **anulable** en `QrTransactions` y `BatchUploads`. `AddBanecoAccountInventory` agrega `BanecoAccounts`, la tabla de concesiones `SubscriberBanecoAccounts` y una columna `BanecoAccountId`, también anulable. `ApiKeyAsGuid` reemplaza `PublicId` y `SecretHash` por una columna `ApiKey uniqueidentifier` única.
**Las claves ya emitidas no sobreviven:** el hash es irreversible, así que cada fila existente recibe un
GUID nuevo con `NEWID()` y hay que redistribuirlo a los suscriptores.

`AddBanecoAccountCredentials` agrega a `BanecoAccounts` las columnas `AuthUserNameEncrypted`, `AuthPasswordEncrypted` y `CredentialsVerifiedAt`, las tres anulables: las cuentas ya existentes siguen funcionando con su token estático de configuración hasta que se les carguen las credenciales. `AddSubscriberSavingsAccount` agrega `Subscribers.SavingsAccountEncrypted`, también anulable.

**Backfill obligatorio de la cuenta de ahorro.** La columna nace nula, de modo que **todo suscriptor existente deja de poder emitir QR** hasta que se le cargue su cuenta con `PATCH /api/admin/subscribers/{id}`. Es deliberado: no hay valor por omisión seguro para "a qué cuenta se acredita el dinero". Recorra `GET /api/admin/subscribers` y complete los que tengan `hasSavingsAccount: false` antes de habilitar el despliegue.

Las filas anteriores quedan con ambas columnas nulas: siguen en la base, pero ningún suscriptor las ve. Si esas transacciones deben quedar visibles, cree el suscriptor y la cuenta que correspondan y haga el backfill antes de habilitar el nuevo esquema. Los índices únicos de `MerchantTransactionId` y `BatchId` pasan a ser compuestos, por suscriptor y por cuenta.

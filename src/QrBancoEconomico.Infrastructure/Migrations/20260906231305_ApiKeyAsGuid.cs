using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QrBancoEconomico.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ApiKeyAsGuid : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // La clave pasa a ser un GUID guardado en claro, para poder cargarla con un INSERT sin
            // pasar por la API. El hash anterior es irreversible, de modo que las claves ya emitidas
            // no se pueden conservar: cada fila existente recibe un GUID nuevo y hay que redistribuirlo.
            migrationBuilder.AddColumnIfMissing("SubscriberApiKeys", "ApiKey", "uniqueidentifier NULL");

            // NEWID() y no NEWSEQUENTIALID(): el segundo produce valores contiguos y adivinables.
            migrationBuilder.Sql("""
                UPDATE [SubscriberApiKeys] SET [ApiKey] = NEWID() WHERE [ApiKey] IS NULL;
                """);

            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[SubscriberApiKeys]')
                           AND name = N'ApiKey' AND is_nullable = 1)
                    ALTER TABLE [SubscriberApiKeys] ALTER COLUMN [ApiKey] uniqueidentifier NOT NULL;
                """);

            migrationBuilder.CreateIndexIfMissing("IX_SubscriberApiKeys_ApiKey", "SubscriberApiKeys",
                "UNIQUE INDEX [IX_SubscriberApiKeys_ApiKey] ON [SubscriberApiKeys] ([ApiKey])");

            migrationBuilder.DropIndexIfPresent("IX_SubscriberApiKeys_PublicId", "SubscriberApiKeys");
            migrationBuilder.DropColumnIfPresent("SubscriberApiKeys", "PublicId");
            migrationBuilder.DropColumnIfPresent("SubscriberApiKeys", "SecretHash");
        }

        /// <inheritdoc />
        /// <remarks>
        /// Revertir restaura las columnas, pero <b>no</b> las claves: los hashes originales se
        /// perdieron al eliminar <c>SecretHash</c>. Tras un <c>Down</c> hay que reemitir todas.
        /// </remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SubscriberApiKeys_ApiKey",
                table: "SubscriberApiKeys");

            migrationBuilder.DropColumn(
                name: "ApiKey",
                table: "SubscriberApiKeys");

            migrationBuilder.AddColumn<string>(
                name: "PublicId",
                table: "SubscriberApiKeys",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<byte[]>(
                name: "SecretHash",
                table: "SubscriberApiKeys",
                type: "varbinary(32)",
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.CreateIndex(
                name: "IX_SubscriberApiKeys_PublicId",
                table: "SubscriberApiKeys",
                column: "PublicId",
                unique: true);
        }
    }
}

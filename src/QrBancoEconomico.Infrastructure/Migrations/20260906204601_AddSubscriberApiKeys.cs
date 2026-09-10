using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QrBancoEconomico.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriberApiKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Los índices globales ceden ante los compuestos por suscriptor: el mismo transactionId
            // puede repetirse entre canales distintos.
            migrationBuilder.DropIndexIfPresent("IX_QrTransactions_MerchantTransactionId", "QrTransactions");
            migrationBuilder.DropIndexIfPresent("IX_BatchUploads_BatchId", "BatchUploads");

            migrationBuilder.CreateTableIfMissing("Subscribers", """
                    [Id] uniqueidentifier NOT NULL,
                    [Code] nvarchar(50) NOT NULL,
                    [Name] nvarchar(200) NOT NULL,
                    [ContactEmail] nvarchar(200) NULL,
                    [IsActive] bit NOT NULL,
                    [RequestsPerMinute] int NOT NULL,
                    [CreatedAt] datetimeoffset NOT NULL,
                    CONSTRAINT [PK_Subscribers] PRIMARY KEY ([Id])
            """);

            migrationBuilder.CreateTableIfMissing("SubscriberApiKeys", """
                    [Id] uniqueidentifier NOT NULL,
                    [SubscriberId] uniqueidentifier NOT NULL,
                    [PublicId] nvarchar(64) NOT NULL,
                    [SecretHash] varbinary(32) NOT NULL,
                    [Environment] nvarchar(16) NOT NULL,
                    [Scopes] nvarchar(512) NOT NULL,
                    [Label] nvarchar(100) NULL,
                    [CreatedAt] datetimeoffset NOT NULL,
                    [ExpiresAt] datetimeoffset NULL,
                    [RevokedAt] datetimeoffset NULL,
                    [LastUsedAt] datetimeoffset NULL,
                    CONSTRAINT [PK_SubscriberApiKeys] PRIMARY KEY ([Id])
            """);

            migrationBuilder.AddColumnIfMissing("QrTransactions", "SubscriberId", "uniqueidentifier NULL");
            migrationBuilder.AddColumnIfMissing("BatchUploads", "SubscriberId", "uniqueidentifier NULL");

            migrationBuilder.CreateIndexIfMissing("IX_QrTransactions_SubscriberId_MerchantTransactionId", "QrTransactions",
                "UNIQUE INDEX [IX_QrTransactions_SubscriberId_MerchantTransactionId] ON [QrTransactions] ([SubscriberId], [MerchantTransactionId]) WHERE [SubscriberId] IS NOT NULL");
            migrationBuilder.CreateIndexIfMissing("IX_BatchUploads_SubscriberId_BatchId", "BatchUploads",
                "UNIQUE INDEX [IX_BatchUploads_SubscriberId_BatchId] ON [BatchUploads] ([SubscriberId], [BatchId]) WHERE [SubscriberId] IS NOT NULL");
            migrationBuilder.CreateIndexIfMissing("IX_SubscriberApiKeys_PublicId", "SubscriberApiKeys",
                "UNIQUE INDEX [IX_SubscriberApiKeys_PublicId] ON [SubscriberApiKeys] ([PublicId])");
            migrationBuilder.CreateIndexIfMissing("IX_SubscriberApiKeys_SubscriberId", "SubscriberApiKeys",
                "INDEX [IX_SubscriberApiKeys_SubscriberId] ON [SubscriberApiKeys] ([SubscriberId])");
            migrationBuilder.CreateIndexIfMissing("IX_Subscribers_Code", "Subscribers",
                "UNIQUE INDEX [IX_Subscribers_Code] ON [Subscribers] ([Code])");

            migrationBuilder.AddForeignKeyIfMissing("FK_SubscriberApiKeys_Subscribers_SubscriberId",
                "SubscriberApiKeys", "SubscriberId", "Subscribers", "Id", "CASCADE");
            migrationBuilder.AddForeignKeyIfMissing("FK_BatchUploads_Subscribers_SubscriberId",
                "BatchUploads", "SubscriberId", "Subscribers", "Id", "NO ACTION");
            migrationBuilder.AddForeignKeyIfMissing("FK_QrTransactions_Subscribers_SubscriberId",
                "QrTransactions", "SubscriberId", "Subscribers", "Id", "NO ACTION");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BatchUploads_Subscribers_SubscriberId",
                table: "BatchUploads");

            migrationBuilder.DropForeignKey(
                name: "FK_QrTransactions_Subscribers_SubscriberId",
                table: "QrTransactions");

            migrationBuilder.DropTable(
                name: "SubscriberApiKeys");

            migrationBuilder.DropTable(
                name: "Subscribers");

            migrationBuilder.DropIndex(
                name: "IX_QrTransactions_SubscriberId_MerchantTransactionId",
                table: "QrTransactions");

            migrationBuilder.DropIndex(
                name: "IX_BatchUploads_SubscriberId_BatchId",
                table: "BatchUploads");

            migrationBuilder.DropColumn(
                name: "SubscriberId",
                table: "QrTransactions");

            migrationBuilder.DropColumn(
                name: "SubscriberId",
                table: "BatchUploads");

            migrationBuilder.CreateIndex(
                name: "IX_QrTransactions_MerchantTransactionId",
                table: "QrTransactions",
                column: "MerchantTransactionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BatchUploads_BatchId",
                table: "BatchUploads",
                column: "BatchId",
                unique: true);
        }
    }
}

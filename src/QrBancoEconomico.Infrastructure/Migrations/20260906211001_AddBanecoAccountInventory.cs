using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QrBancoEconomico.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBanecoAccountInventory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTableIfMissing("BanecoAccounts", """
                    [Id] uniqueidentifier NOT NULL,
                    [Code] nvarchar(50) NOT NULL,
                    [Name] nvarchar(200) NOT NULL,
                    [CredentialRef] nvarchar(100) NULL,
                    [AccountCodeEncrypted] nvarchar(1024) NULL,
                    [BatchDebitAccountEncrypted] nvarchar(1024) NULL,
                    [IsActive] bit NOT NULL,
                    [CreatedAt] datetimeoffset NOT NULL,
                    CONSTRAINT [PK_BanecoAccounts] PRIMARY KEY ([Id])
            """);

            migrationBuilder.CreateTableIfMissing("SubscriberBanecoAccounts", """
                    [SubscriberId] uniqueidentifier NOT NULL,
                    [BanecoAccountId] uniqueidentifier NOT NULL,
                    [IsDefault] bit NOT NULL,
                    [GrantedAt] datetimeoffset NOT NULL,
                    [GrantedBy] nvarchar(50) NULL,
                    CONSTRAINT [PK_SubscriberBanecoAccounts] PRIMARY KEY ([SubscriberId], [BanecoAccountId])
            """);

            migrationBuilder.AddColumnIfMissing("QrTransactions", "BanecoAccountId", "uniqueidentifier NULL");
            migrationBuilder.AddColumnIfMissing("BatchUploads", "BanecoAccountId", "uniqueidentifier NULL");

            migrationBuilder.CreateIndexIfMissing("IX_QrTransactions_BanecoAccountId_MerchantTransactionId", "QrTransactions",
                "UNIQUE INDEX [IX_QrTransactions_BanecoAccountId_MerchantTransactionId] ON [QrTransactions] ([BanecoAccountId], [MerchantTransactionId]) WHERE [BanecoAccountId] IS NOT NULL");
            migrationBuilder.CreateIndexIfMissing("IX_BatchUploads_BanecoAccountId_BatchId", "BatchUploads",
                "UNIQUE INDEX [IX_BatchUploads_BanecoAccountId_BatchId] ON [BatchUploads] ([BanecoAccountId], [BatchId]) WHERE [BanecoAccountId] IS NOT NULL");
            migrationBuilder.CreateIndexIfMissing("IX_BanecoAccounts_Code", "BanecoAccounts",
                "UNIQUE INDEX [IX_BanecoAccounts_Code] ON [BanecoAccounts] ([Code])");
            migrationBuilder.CreateIndexIfMissing("IX_SubscriberBanecoAccounts_BanecoAccountId", "SubscriberBanecoAccounts",
                "INDEX [IX_SubscriberBanecoAccounts_BanecoAccountId] ON [SubscriberBanecoAccounts] ([BanecoAccountId])");
            // Como máximo una cuenta predeterminada por suscriptor.
            migrationBuilder.CreateIndexIfMissing("UX_SubscriberBanecoAccounts_Default", "SubscriberBanecoAccounts",
                "UNIQUE INDEX [UX_SubscriberBanecoAccounts_Default] ON [SubscriberBanecoAccounts] ([SubscriberId]) WHERE [IsDefault] = 1");

            migrationBuilder.AddForeignKeyIfMissing("FK_SubscriberBanecoAccounts_BanecoAccounts_BanecoAccountId",
                "SubscriberBanecoAccounts", "BanecoAccountId", "BanecoAccounts", "Id", "CASCADE");
            migrationBuilder.AddForeignKeyIfMissing("FK_SubscriberBanecoAccounts_Subscribers_SubscriberId",
                "SubscriberBanecoAccounts", "SubscriberId", "Subscribers", "Id", "CASCADE");
            migrationBuilder.AddForeignKeyIfMissing("FK_BatchUploads_BanecoAccounts_BanecoAccountId",
                "BatchUploads", "BanecoAccountId", "BanecoAccounts", "Id", "NO ACTION");
            migrationBuilder.AddForeignKeyIfMissing("FK_QrTransactions_BanecoAccounts_BanecoAccountId",
                "QrTransactions", "BanecoAccountId", "BanecoAccounts", "Id", "NO ACTION");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BatchUploads_BanecoAccounts_BanecoAccountId",
                table: "BatchUploads");

            migrationBuilder.DropForeignKey(
                name: "FK_QrTransactions_BanecoAccounts_BanecoAccountId",
                table: "QrTransactions");

            migrationBuilder.DropTable(
                name: "SubscriberBanecoAccounts");

            migrationBuilder.DropTable(
                name: "BanecoAccounts");

            migrationBuilder.DropIndex(
                name: "IX_QrTransactions_BanecoAccountId_MerchantTransactionId",
                table: "QrTransactions");

            migrationBuilder.DropIndex(
                name: "IX_BatchUploads_BanecoAccountId_BatchId",
                table: "BatchUploads");

            migrationBuilder.DropColumn(
                name: "BanecoAccountId",
                table: "QrTransactions");

            migrationBuilder.DropColumn(
                name: "BanecoAccountId",
                table: "BatchUploads");
        }
    }
}

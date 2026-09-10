using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QrBancoEconomico.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTableIfMissing("BatchUploads", """
                    [Id] uniqueidentifier NOT NULL,
                    [BatchId] nvarchar(450) NOT NULL,
                    [Type] nvarchar(max) NOT NULL,
                    [Description] nvarchar(max) NOT NULL,
                    [DetailedDebit] bit NOT NULL,
                    [AccountCodeEncrypted] nvarchar(max) NOT NULL,
                    [Currency] nvarchar(max) NOT NULL,
                    [Amount] decimal(18,2) NOT NULL,
                    [PaymentCount] int NOT NULL,
                    [BankBatchId] bigint NULL,
                    [CreatedAt] datetimeoffset NOT NULL,
                    CONSTRAINT [PK_BatchUploads] PRIMARY KEY ([Id])
            """);

            migrationBuilder.CreateTableIfMissing("QrTransactions", """
                    [Id] uniqueidentifier NOT NULL,
                    [MerchantTransactionId] nvarchar(100) NOT NULL,
                    [BankQrId] nvarchar(450) NULL,
                    [AccountCreditEncrypted] nvarchar(1024) NOT NULL,
                    [Currency] nvarchar(3) NOT NULL,
                    [Amount] decimal(18,2) NOT NULL,
                    [Description] nvarchar(max) NULL,
                    [DueDate] date NOT NULL,
                    [SingleUse] bit NOT NULL,
                    [ModifyAmount] bit NOT NULL,
                    [BranchCode] nvarchar(max) NULL,
                    [Status] int NOT NULL,
                    [QrImageBase64] nvarchar(max) NULL,
                    [CreatedAt] datetimeoffset NOT NULL,
                    CONSTRAINT [PK_QrTransactions] PRIMARY KEY ([Id])
            """);

            migrationBuilder.CreateTableIfMissing("QrPayments", """
                    [Id] uniqueidentifier NOT NULL,
                    [QrTransactionId] uniqueidentifier NOT NULL,
                    [QrId] nvarchar(100) NOT NULL,
                    [TransactionId] nvarchar(max) NULL,
                    [PaymentDate] date NOT NULL,
                    [PaymentTime] nvarchar(max) NULL,
                    [Currency] nvarchar(max) NOT NULL,
                    [Amount] decimal(18,2) NOT NULL,
                    [SenderBankCode] nvarchar(max) NULL,
                    [SenderName] nvarchar(max) NULL,
                    [SenderDocumentId] nvarchar(max) NULL,
                    [SenderAccount] nvarchar(max) NULL,
                    [Description] nvarchar(max) NULL,
                    [BranchCode] nvarchar(max) NULL,
                    [ReceivedAt] datetimeoffset NOT NULL,
                    CONSTRAINT [PK_QrPayments] PRIMARY KEY ([Id])
            """);

            migrationBuilder.AddForeignKeyIfMissing("FK_QrPayments_QrTransactions_QrTransactionId",
                "QrPayments", "QrTransactionId", "QrTransactions", "Id", "CASCADE");

            migrationBuilder.CreateIndexIfMissing("IX_BatchUploads_BatchId", "BatchUploads",
                "UNIQUE INDEX [IX_BatchUploads_BatchId] ON [BatchUploads] ([BatchId])");
            migrationBuilder.CreateIndexIfMissing("IX_QrPayments_QrTransactionId", "QrPayments",
                "INDEX [IX_QrPayments_QrTransactionId] ON [QrPayments] ([QrTransactionId])");
            migrationBuilder.CreateIndexIfMissing("IX_QrTransactions_BankQrId", "QrTransactions",
                "UNIQUE INDEX [IX_QrTransactions_BankQrId] ON [QrTransactions] ([BankQrId]) WHERE [BankQrId] IS NOT NULL");
            migrationBuilder.CreateIndexIfMissing("IX_QrTransactions_MerchantTransactionId", "QrTransactions",
                "UNIQUE INDEX [IX_QrTransactions_MerchantTransactionId] ON [QrTransactions] ([MerchantTransactionId])");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BatchUploads");

            migrationBuilder.DropTable(
                name: "QrPayments");

            migrationBuilder.DropTable(
                name: "QrTransactions");
        }
    }
}

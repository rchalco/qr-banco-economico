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
            migrationBuilder.CreateTable(
                name: "BatchUploads",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BatchId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DetailedDebit = table.Column<bool>(type: "bit", nullable: false),
                    AccountCodeEncrypted = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    PaymentCount = table.Column<int>(type: "int", nullable: false),
                    BankBatchId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BatchUploads", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "QrTransactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MerchantTransactionId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    BankQrId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    AccountCreditEncrypted = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    SingleUse = table.Column<bool>(type: "bit", nullable: false),
                    ModifyAmount = table.Column<bool>(type: "bit", nullable: false),
                    BranchCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    QrImageBase64 = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QrTransactions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "QrPayments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QrTransactionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QrId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TransactionId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PaymentDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PaymentTime = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Currency = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    SenderBankCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SenderName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SenderDocumentId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SenderAccount = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    BranchCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QrPayments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QrPayments_QrTransactions_QrTransactionId",
                        column: x => x.QrTransactionId,
                        principalTable: "QrTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BatchUploads_BatchId",
                table: "BatchUploads",
                column: "BatchId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QrPayments_QrTransactionId",
                table: "QrPayments",
                column: "QrTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_QrTransactions_BankQrId",
                table: "QrTransactions",
                column: "BankQrId",
                unique: true,
                filter: "[BankQrId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_QrTransactions_MerchantTransactionId",
                table: "QrTransactions",
                column: "MerchantTransactionId",
                unique: true);
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

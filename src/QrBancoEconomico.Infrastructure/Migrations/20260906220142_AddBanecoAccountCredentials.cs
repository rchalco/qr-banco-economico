using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QrBancoEconomico.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBanecoAccountCredentials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumnIfMissing("BanecoAccounts", "AuthUserNameEncrypted", "nvarchar(512) NULL");
            migrationBuilder.AddColumnIfMissing("BanecoAccounts", "AuthPasswordEncrypted", "nvarchar(1024) NULL");
            migrationBuilder.AddColumnIfMissing("BanecoAccounts", "CredentialsVerifiedAt", "datetimeoffset NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AuthPasswordEncrypted",
                table: "BanecoAccounts");

            migrationBuilder.DropColumn(
                name: "AuthUserNameEncrypted",
                table: "BanecoAccounts");

            migrationBuilder.DropColumn(
                name: "CredentialsVerifiedAt",
                table: "BanecoAccounts");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QrBancoEconomico.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriberSavingsAccount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumnIfMissing("Subscribers", "SavingsAccountEncrypted", "nvarchar(1024) NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SavingsAccountEncrypted",
                table: "Subscribers");
        }
    }
}

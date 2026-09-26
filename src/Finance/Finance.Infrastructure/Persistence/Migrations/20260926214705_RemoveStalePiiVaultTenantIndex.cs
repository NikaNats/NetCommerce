using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NetCommerce.Finance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveStalePiiVaultTenantIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_pii_vault_entries_TenantId",
                schema: "finance",
                table: "pii_vault_entries");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_pii_vault_entries_TenantId",
                schema: "finance",
                table: "pii_vault_entries",
                column: "TenantId");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ordering.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantIdToOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "tenant_id",
                schema: "ordering",
                table: "orders",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "default-tenant");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_TenantId",
                schema: "ordering",
                table: "orders",
                column: "tenant_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Orders_TenantId",
                schema: "ordering",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "tenant_id",
                schema: "ordering",
                table: "orders");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "inventory");

            migrationBuilder.CreateTable(
                name: "stocks",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    low_stock_threshold = table.Column<int>(type: "integer", nullable: false, defaultValue: 10),
                    warehouse_location = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    last_updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false, defaultValue: 0u)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stocks", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "stock_reservations",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    stock_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    confirmed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    released_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_reservations", x => x.id);
                    table.ForeignKey(
                        name: "FK_stock_reservations_stocks_stock_id",
                        column: x => x.stock_id,
                        principalSchema: "inventory",
                        principalTable: "stocks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_stock_reservations_order_id",
                schema: "inventory",
                table: "stock_reservations",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "IX_stock_reservations_status_expires_at",
                schema: "inventory",
                table: "stock_reservations",
                columns: new[] { "status", "expires_at" },
                filter: "status = 'Active'");

            migrationBuilder.CreateIndex(
                name: "IX_stock_reservations_status_updated_at",
                schema: "inventory",
                table: "stock_reservations",
                columns: new[] { "status", "updated_at" },
                filter: "status = 'PendingPayment'");

            migrationBuilder.CreateIndex(
                name: "IX_stock_reservations_stock_id",
                schema: "inventory",
                table: "stock_reservations",
                column: "stock_id");

            migrationBuilder.CreateIndex(
                name: "IX_stocks_product_id",
                schema: "inventory",
                table: "stocks",
                column: "product_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_stocks_sku",
                schema: "inventory",
                table: "stocks",
                column: "sku",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "stock_reservations",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "stocks",
                schema: "inventory");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shipping.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "shipping");

            migrationBuilder.CreateTable(
                name: "shipments",
                schema: "shipping",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    TrackingNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CourierProvider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    WeightKg = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    length_cm = table.Column<decimal>(type: "numeric", nullable: true),
                    width_cm = table.Column<decimal>(type: "numeric", nullable: true),
                    height_cm = table.Column<decimal>(type: "numeric", nullable: true),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    recipient_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    street = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    state = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    country = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    postal_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    phone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PickedUpAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeliveredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EstimatedDeliveryDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FailureReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shipments", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_shipments_CourierProvider",
                schema: "shipping",
                table: "shipments",
                column: "CourierProvider");

            migrationBuilder.CreateIndex(
                name: "IX_shipments_OrderId",
                schema: "shipping",
                table: "shipments",
                column: "OrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_shipments_Status",
                schema: "shipping",
                table: "shipments",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_shipments_Status_CreatedAt",
                schema: "shipping",
                table: "shipments",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_shipments_TrackingNumber",
                schema: "shipping",
                table: "shipments",
                column: "TrackingNumber",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "shipments",
                schema: "shipping");
        }
    }
}

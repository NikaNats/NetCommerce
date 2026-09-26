using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ordering.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantIdToOrder : Migration
    {
        // NOTE: This migration is intentionally a no-op. The tenant_id column
        // and its index are created by the 20260203000000_BaselineOrderSchema
        // baseline, which was added later to repair a history that previously
        // contained no table creation at all. The empty Up/Down keeps the
        // migration history chain (and any database that recorded it) intact
        // while making scripted provisioning from zero idempotent.
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}

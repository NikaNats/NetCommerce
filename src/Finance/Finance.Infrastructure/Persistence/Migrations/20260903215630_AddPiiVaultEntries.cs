using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NetCommerce.Finance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPiiVaultEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "pii_vault_entries",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EncryptedFullName = table.Column<string>(type: "text", nullable: false),
                    EncryptedEmail = table.Column<string>(type: "text", nullable: false),
                    EmailBlindIndex = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    EncryptedPhoneNumber = table.Column<string>(type: "text", nullable: false),
                    PhoneBlindIndex = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    EncryptedAddress = table.Column<string>(type: "text", nullable: false),
                    EncryptedDateOfBirth = table.Column<string>(type: "text", nullable: true),
                    EncryptedNationalId = table.Column<string>(type: "text", nullable: true),
                    ProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    TenantId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    DeletedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastAccessedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    KeyVersion = table.Column<int>(type: "integer", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pii_vault_entries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_pii_vault_entries_profile_id",
                schema: "finance",
                table: "pii_vault_entries",
                column: "ProfileId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pii_vault_entries_tenant_email_index",
                schema: "finance",
                table: "pii_vault_entries",
                columns: new[] { "TenantId", "EmailBlindIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pii_vault_entries_tenant_phone_index",
                schema: "finance",
                table: "pii_vault_entries",
                columns: new[] { "TenantId", "PhoneBlindIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_pii_vault_entries_TenantId",
                schema: "finance",
                table: "pii_vault_entries",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "ix_pii_vault_entries_user_id",
                schema: "finance",
                table: "pii_vault_entries",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pii_vault_entries",
                schema: "finance");
        }
    }
}

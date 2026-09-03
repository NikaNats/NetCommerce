using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NetCommerce.Finance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "finance");

            migrationBuilder.CreateTable(
                name: "financial_audit_log",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AuditType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    EntityType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EntityId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    external_transaction_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    PreviousState = table.Column<string>(type: "jsonb", nullable: true),
                    NewState = table.Column<string>(type: "jsonb", nullable: true),
                    ActorId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ActorType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Metadata = table.Column<string>(type: "jsonb", nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    correlation_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_financial_audit_log", x => x.Id);
                },
                comment: "Immutable audit log - INSERT only, no UPDATE/DELETE");

            migrationBuilder.CreateTable(
                name: "processed_webhook_events",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StripeEventId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EventType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PaymentIntentId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_processed_webhook_events", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReconciliationSessions",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CalculatedForDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    TotalInternalAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalExternalAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReconciliationSessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReconciliationDiscrepancies",
                schema: "finance",
                columns: table => new
                {
                    ExternalTxnId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DetectedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReconciliationSessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "text", nullable: false),
                    Difference = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReconciliationDiscrepancies", x => new { x.ReconciliationSessionId, x.ExternalTxnId, x.DetectedAt });
                    table.ForeignKey(
                        name: "FK_ReconciliationDiscrepancies_ReconciliationSessions_Reconcil~",
                        column: x => x.ReconciliationSessionId,
                        principalSchema: "finance",
                        principalTable: "ReconciliationSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_financial_audit_correlation",
                schema: "finance",
                table: "financial_audit_log",
                column: "correlation_id",
                filter: "correlation_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_financial_audit_entity",
                schema: "finance",
                table: "financial_audit_log",
                columns: new[] { "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "ix_financial_audit_external_txn",
                schema: "finance",
                table: "financial_audit_log",
                column: "external_transaction_id",
                filter: "external_transaction_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_financial_audit_occurred_at",
                schema: "finance",
                table: "financial_audit_log",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "ix_financial_audit_type",
                schema: "finance",
                table: "financial_audit_log",
                column: "AuditType");

            migrationBuilder.CreateIndex(
                name: "ix_processed_webhook_events_payment_intent_id",
                schema: "finance",
                table: "processed_webhook_events",
                column: "PaymentIntentId");

            migrationBuilder.CreateIndex(
                name: "ix_processed_webhook_events_received_at",
                schema: "finance",
                table: "processed_webhook_events",
                column: "ReceivedAt");

            migrationBuilder.CreateIndex(
                name: "ix_processed_webhook_events_stripe_event_id",
                schema: "finance",
                table: "processed_webhook_events",
                column: "StripeEventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationSessions_CalculatedForDate",
                schema: "finance",
                table: "ReconciliationSessions",
                column: "CalculatedForDate");

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationSessions_Status",
                schema: "finance",
                table: "ReconciliationSessions",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "financial_audit_log",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "processed_webhook_events",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "ReconciliationDiscrepancies",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "ReconciliationSessions",
                schema: "finance");
        }
    }
}

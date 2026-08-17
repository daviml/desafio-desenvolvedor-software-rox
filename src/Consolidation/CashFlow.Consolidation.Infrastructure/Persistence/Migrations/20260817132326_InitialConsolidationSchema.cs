using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CashFlow.Consolidation.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialConsolidationSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "consolidation");

            migrationBuilder.CreateTable(
                name: "daily_balances",
                schema: "consolidation",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    merchant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    credit_count = table.Column<int>(type: "integer", nullable: false),
                    debit_count = table.Column<int>(type: "integer", nullable: false),
                    last_updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    total_credits = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total_credits_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    total_debits = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total_debits_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_daily_balances", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "processed_events",
                schema: "consolidation",
                columns: table => new
                {
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    processed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_processed_events", x => x.event_id);
                });

            migrationBuilder.CreateIndex(
                name: "ux_daily_balances_merchant_id_date",
                schema: "consolidation",
                table: "daily_balances",
                columns: new[] { "merchant_id", "date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_processed_events_processed_at_utc",
                schema: "consolidation",
                table: "processed_events",
                column: "processed_at_utc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "daily_balances",
                schema: "consolidation");

            migrationBuilder.DropTable(
                name: "processed_events",
                schema: "consolidation");
        }
    }
}

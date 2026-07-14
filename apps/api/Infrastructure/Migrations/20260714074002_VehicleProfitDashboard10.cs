using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DealerOS.Api.Infrastructure.Migrations;

/// <inheritdoc />
public partial class VehicleProfitDashboard10 : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "finance");

        migrationBuilder.CreateTable(
            name: "manual_cost_entries",
            schema: "finance",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                VehicleId = table.Column<Guid>(type: "uuid", nullable: false),
                DealId = table.Column<Guid>(type: "uuid", nullable: false),
                CommandId = table.Column<Guid>(type: "uuid", nullable: false),
                Signature = table.Column<string>(type: "text", nullable: false),
                Category = table.Column<int>(type: "integer", nullable: false),
                Source = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                Reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Amount = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: false),
                Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                Evidence = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                Comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                SupersedesEntryId = table.Column<Guid>(type: "uuid", nullable: true),
                CorrectionReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_manual_cost_entries", x => x.Id);
                table.UniqueConstraint("ak_finance_cost_entries_organization_id", x => new { x.OrganizationId, x.Id });
                table.CheckConstraint("ck_finance_cost_amount", "\"Amount\" > 0");
                table.CheckConstraint("ck_finance_cost_category", "\"Category\" BETWEEN 1 AND 9");
                table.CheckConstraint("ck_finance_cost_correction", "\"SupersedesEntryId\" IS NULL OR \"SupersedesEntryId\" <> \"Id\"");
                table.CheckConstraint("ck_finance_cost_currency", "\"Currency\" ~ '^[A-Z]{3}$'");
                table.ForeignKey(
                    name: "FK_manual_cost_entries_branches_OrganizationId_BranchId",
                    columns: x => new { x.OrganizationId, x.BranchId },
                    principalSchema: "organizations",
                    principalTable: "branches",
                    principalColumns: new[] { "OrganizationId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_manual_cost_entries_deals_OrganizationId_DealId",
                    columns: x => new { x.OrganizationId, x.DealId },
                    principalSchema: "deals",
                    principalTable: "deals",
                    principalColumns: new[] { "OrganizationId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_manual_cost_entries_manual_cost_entries_OrganizationId_Supe~",
                    columns: x => new { x.OrganizationId, x.SupersedesEntryId },
                    principalSchema: "finance",
                    principalTable: "manual_cost_entries",
                    principalColumns: new[] { "OrganizationId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_manual_cost_entries_users_OrganizationId_ActorUserId",
                    columns: x => new { x.OrganizationId, x.ActorUserId },
                    principalSchema: "identity",
                    principalTable: "users",
                    principalColumns: new[] { "OrganizationId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_manual_cost_entries_vehicles_OrganizationId_VehicleId",
                    columns: x => new { x.OrganizationId, x.VehicleId },
                    principalSchema: "vehicles",
                    principalTable: "vehicles",
                    principalColumns: new[] { "OrganizationId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "profit_snapshots",
            schema: "finance",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                DealId = table.Column<Guid>(type: "uuid", nullable: false),
                VehicleId = table.Column<Guid>(type: "uuid", nullable: false),
                Revision = table.Column<int>(type: "integer", nullable: false),
                RevisesSnapshotId = table.Column<Guid>(type: "uuid", nullable: true),
                Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                FormulaVersion = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                GrossRevenue = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: false),
                Refunds = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: false),
                NetRevenue = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: false),
                PurchaseCost = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: false),
                OperationsCost = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: false),
                ManualCost = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: false),
                TotalCost = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: false),
                ActualProfit = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: false),
                ActualMarginPercent = table.Column<decimal>(type: "numeric(9,2)", precision: 9, scale: 2, nullable: true),
                PlanProfit = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: false),
                PlanMarginPercent = table.Column<decimal>(type: "numeric(9,2)", precision: 9, scale: 2, nullable: true),
                SourcesJson = table.Column<string>(type: "jsonb", nullable: false),
                Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_profit_snapshots", x => x.Id);
                table.UniqueConstraint("ak_finance_profit_snapshots_organization_id", x => new { x.OrganizationId, x.Id });
                table.CheckConstraint("ck_finance_profit_currency", "\"Currency\" ~ '^[A-Z]{3}$'");
                table.CheckConstraint("ck_finance_profit_nonnegative", "\"GrossRevenue\" >= 0 AND \"Refunds\" >= 0 AND \"NetRevenue\" >= 0 AND \"PurchaseCost\" >= 0 AND \"OperationsCost\" >= 0 AND \"ManualCost\" >= 0 AND \"TotalCost\" >= 0");
                table.CheckConstraint("ck_finance_profit_revision", "\"Revision\" > 0");
                table.ForeignKey(
                    name: "FK_profit_snapshots_branches_OrganizationId_BranchId",
                    columns: x => new { x.OrganizationId, x.BranchId },
                    principalSchema: "organizations",
                    principalTable: "branches",
                    principalColumns: new[] { "OrganizationId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_profit_snapshots_deals_OrganizationId_DealId",
                    columns: x => new { x.OrganizationId, x.DealId },
                    principalSchema: "deals",
                    principalTable: "deals",
                    principalColumns: new[] { "OrganizationId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_profit_snapshots_profit_snapshots_OrganizationId_RevisesSna~",
                    columns: x => new { x.OrganizationId, x.RevisesSnapshotId },
                    principalSchema: "finance",
                    principalTable: "profit_snapshots",
                    principalColumns: new[] { "OrganizationId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_profit_snapshots_users_OrganizationId_CreatedByUserId",
                    columns: x => new { x.OrganizationId, x.CreatedByUserId },
                    principalSchema: "identity",
                    principalTable: "users",
                    principalColumns: new[] { "OrganizationId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_profit_snapshots_vehicles_OrganizationId_VehicleId",
                    columns: x => new { x.OrganizationId, x.VehicleId },
                    principalSchema: "vehicles",
                    principalTable: "vehicles",
                    principalColumns: new[] { "OrganizationId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "ix_finance_cost_deal_date",
            schema: "finance",
            table: "manual_cost_entries",
            columns: new[] { "OrganizationId", "DealId", "OccurredAt" });

        migrationBuilder.CreateIndex(
            name: "IX_manual_cost_entries_OrganizationId_ActorUserId",
            schema: "finance",
            table: "manual_cost_entries",
            columns: new[] { "OrganizationId", "ActorUserId" });

        migrationBuilder.CreateIndex(
            name: "IX_manual_cost_entries_OrganizationId_BranchId",
            schema: "finance",
            table: "manual_cost_entries",
            columns: new[] { "OrganizationId", "BranchId" });

        migrationBuilder.CreateIndex(
            name: "IX_manual_cost_entries_OrganizationId_VehicleId",
            schema: "finance",
            table: "manual_cost_entries",
            columns: new[] { "OrganizationId", "VehicleId" });

        migrationBuilder.CreateIndex(
            name: "ux_finance_cost_command",
            schema: "finance",
            table: "manual_cost_entries",
            columns: new[] { "OrganizationId", "CommandId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ux_finance_cost_correction",
            schema: "finance",
            table: "manual_cost_entries",
            columns: new[] { "OrganizationId", "SupersedesEntryId" },
            unique: true,
            filter: "\"SupersedesEntryId\" IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "ux_finance_cost_reference",
            schema: "finance",
            table: "manual_cost_entries",
            columns: new[] { "OrganizationId", "Reference" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_finance_profit_dashboard",
            schema: "finance",
            table: "profit_snapshots",
            columns: new[] { "OrganizationId", "BranchId", "Currency", "CreatedAt" });

        migrationBuilder.CreateIndex(
            name: "IX_profit_snapshots_OrganizationId_CreatedByUserId",
            schema: "finance",
            table: "profit_snapshots",
            columns: new[] { "OrganizationId", "CreatedByUserId" });

        migrationBuilder.CreateIndex(
            name: "IX_profit_snapshots_OrganizationId_RevisesSnapshotId",
            schema: "finance",
            table: "profit_snapshots",
            columns: new[] { "OrganizationId", "RevisesSnapshotId" });

        migrationBuilder.CreateIndex(
            name: "IX_profit_snapshots_OrganizationId_VehicleId",
            schema: "finance",
            table: "profit_snapshots",
            columns: new[] { "OrganizationId", "VehicleId" });

        migrationBuilder.CreateIndex(
            name: "ux_finance_profit_deal_revision",
            schema: "finance",
            table: "profit_snapshots",
            columns: new[] { "OrganizationId", "DealId", "Revision" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "manual_cost_entries",
            schema: "finance");

        migrationBuilder.DropTable(
            name: "profit_snapshots",
            schema: "finance");
    }
}

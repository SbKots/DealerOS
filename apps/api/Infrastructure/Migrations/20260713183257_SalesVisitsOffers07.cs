using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DealerOS.Api.Infrastructure.Migrations;

/// <inheritdoc />
public partial class SalesVisitsOffers07 : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "sales");

        migrationBuilder.AddColumn<decimal>(
            name: "SalesAutoApprovalDiscountLimit",
            schema: "organizations",
            table: "organizations",
            type: "numeric(19,2)",
            precision: 19,
            scale: 2,
            nullable: false,
            defaultValue: 50000m);

        migrationBuilder.AddColumn<decimal>(
            name: "SalesMinimumMarginAmount",
            schema: "organizations",
            table: "organizations",
            type: "numeric(19,2)",
            precision: 19,
            scale: 2,
            nullable: false,
            defaultValue: 100000m);

        migrationBuilder.Sql(
            """
            ALTER TABLE organizations.organizations
                ADD CONSTRAINT ck_organizations_sales_policy
                CHECK ("SalesAutoApprovalDiscountLimit" >= 0 AND "SalesMinimumMarginAmount" >= 0);
            """);

        migrationBuilder.CreateTable(
            name: "offers",
            schema: "sales",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                LeadId = table.Column<Guid>(type: "uuid", nullable: false),
                VehicleId = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                RevisesOfferId = table.Column<Guid>(type: "uuid", nullable: true),
                CreateSignature = table.Column<string>(type: "text", nullable: false),
                Revision = table.Column<int>(type: "integer", nullable: false),
                Status = table.Column<int>(type: "integer", nullable: false),
                BasePriceAmount = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: false),
                DiscountAmount = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: false),
                CostSnapshotAmount = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: false),
                MinimumMarginAmount = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: false),
                Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                ValidUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                Version = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_offers", x => x.Id);
                table.UniqueConstraint("ak_sales_offers_organization_id", x => new { x.OrganizationId, x.Id });
                table.CheckConstraint("ck_sales_offer_amounts", "\"BasePriceAmount\" > 0 AND \"DiscountAmount\" >= 0 AND \"CostSnapshotAmount\" >= 0 AND \"MinimumMarginAmount\" >= 0");
                table.CheckConstraint("ck_sales_offer_revision", "\"Revision\" > 0");
                table.CheckConstraint("ck_sales_offer_status", "\"Status\" BETWEEN 1 AND 7");
                table.CheckConstraint("ck_sales_offer_version", "\"Version\" > 0");
                table.ForeignKey(
                    name: "FK_offers_branches_OrganizationId_BranchId",
                    columns: x => new { x.OrganizationId, x.BranchId },
                    principalSchema: "organizations",
                    principalTable: "branches",
                    principalColumns: ["OrganizationId", "Id"],
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_offers_customers_OrganizationId_CustomerId",
                    columns: x => new { x.OrganizationId, x.CustomerId },
                    principalSchema: "crm",
                    principalTable: "customers",
                    principalColumns: ["OrganizationId", "Id"],
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_offers_leads_OrganizationId_LeadId",
                    columns: x => new { x.OrganizationId, x.LeadId },
                    principalSchema: "crm",
                    principalTable: "leads",
                    principalColumns: ["OrganizationId", "Id"],
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_offers_offers_OrganizationId_RevisesOfferId",
                    columns: x => new { x.OrganizationId, x.RevisesOfferId },
                    principalSchema: "sales",
                    principalTable: "offers",
                    principalColumns: ["OrganizationId", "Id"],
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_offers_users_OrganizationId_CreatedByUserId",
                    columns: x => new { x.OrganizationId, x.CreatedByUserId },
                    principalSchema: "identity",
                    principalTable: "users",
                    principalColumns: ["OrganizationId", "Id"],
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_offers_vehicles_OrganizationId_VehicleId",
                    columns: x => new { x.OrganizationId, x.VehicleId },
                    principalSchema: "vehicles",
                    principalTable: "vehicles",
                    principalColumns: ["OrganizationId", "Id"],
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "visits",
            schema: "sales",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                LeadId = table.Column<Guid>(type: "uuid", nullable: false),
                VehicleId = table.Column<Guid>(type: "uuid", nullable: false),
                ResponsibleUserId = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                CreateSignature = table.Column<string>(type: "text", nullable: false),
                Status = table.Column<int>(type: "integer", nullable: false),
                StartsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                EndsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                IncludesTestDrive = table.Column<bool>(type: "boolean", nullable: false),
                DriverDocumentsChecked = table.Column<bool>(type: "boolean", nullable: false),
                IssueChecklist = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                ReturnChecklist = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                OdometerOutKm = table.Column<int>(type: "integer", nullable: true),
                OdometerInKm = table.Column<int>(type: "integer", nullable: true),
                ConditionOut = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                ConditionIn = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                CheckedOutAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                CheckedInAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                IncidentOccurred = table.Column<bool>(type: "boolean", nullable: false),
                IncidentComment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                Result = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                NextAction = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                NextActionDueAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                ClosureReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                Version = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_visits", x => x.Id);
                table.UniqueConstraint("ak_sales_visits_organization_id", x => new { x.OrganizationId, x.Id });
                table.CheckConstraint("ck_sales_visit_check_in", "\"CheckedInAt\" IS NULL OR (\"CheckedOutAt\" IS NOT NULL AND \"ReturnChecklist\" IS NOT NULL AND \"OdometerInKm\" IS NOT NULL AND \"ConditionIn\" IS NOT NULL)");
                table.CheckConstraint("ck_sales_visit_incident", "NOT \"IncidentOccurred\" OR \"IncidentComment\" IS NOT NULL");
                table.CheckConstraint("ck_sales_visit_odometer", "\"OdometerOutKm\" IS NULL OR (\"OdometerOutKm\" >= 0 AND (\"OdometerInKm\" IS NULL OR \"OdometerInKm\" >= \"OdometerOutKm\"))");
                table.CheckConstraint("ck_sales_visit_slot", "\"EndsAt\" > \"StartsAt\"");
                table.CheckConstraint("ck_sales_visit_status", "\"Status\" BETWEEN 1 AND 5");
                table.CheckConstraint("ck_sales_visit_test_drive", "\"CheckedOutAt\" IS NULL OR (\"IncludesTestDrive\" AND \"DriverDocumentsChecked\" AND \"IssueChecklist\" IS NOT NULL AND \"OdometerOutKm\" IS NOT NULL AND \"ConditionOut\" IS NOT NULL)");
                table.CheckConstraint("ck_sales_visit_version", "\"Version\" > 0");
                table.ForeignKey(
                    name: "FK_visits_branches_OrganizationId_BranchId",
                    columns: x => new { x.OrganizationId, x.BranchId },
                    principalSchema: "organizations",
                    principalTable: "branches",
                    principalColumns: ["OrganizationId", "Id"],
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_visits_customers_OrganizationId_CustomerId",
                    columns: x => new { x.OrganizationId, x.CustomerId },
                    principalSchema: "crm",
                    principalTable: "customers",
                    principalColumns: ["OrganizationId", "Id"],
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_visits_leads_OrganizationId_LeadId",
                    columns: x => new { x.OrganizationId, x.LeadId },
                    principalSchema: "crm",
                    principalTable: "leads",
                    principalColumns: ["OrganizationId", "Id"],
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_visits_users_OrganizationId_CreatedByUserId",
                    columns: x => new { x.OrganizationId, x.CreatedByUserId },
                    principalSchema: "identity",
                    principalTable: "users",
                    principalColumns: ["OrganizationId", "Id"],
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_visits_users_OrganizationId_ResponsibleUserId",
                    columns: x => new { x.OrganizationId, x.ResponsibleUserId },
                    principalSchema: "identity",
                    principalTable: "users",
                    principalColumns: ["OrganizationId", "Id"],
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_visits_vehicles_OrganizationId_VehicleId",
                    columns: x => new { x.OrganizationId, x.VehicleId },
                    principalSchema: "vehicles",
                    principalTable: "vehicles",
                    principalColumns: ["OrganizationId", "Id"],
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "approved_offer_snapshots",
            schema: "sales",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                OfferId = table.Column<Guid>(type: "uuid", nullable: false),
                Revision = table.Column<int>(type: "integer", nullable: false),
                BasePriceAmount = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: false),
                LineItemsAmount = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: false),
                DiscountAmount = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: false),
                FinalPriceAmount = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: false),
                CostSnapshotAmount = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: false),
                ExpectedMarginAmount = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: false),
                MinimumMarginAmount = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: false),
                Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                ValidUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                LineItemsJson = table.Column<string>(type: "jsonb", nullable: false),
                ApprovedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                ApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_approved_offer_snapshots", x => x.Id);
                table.CheckConstraint("ck_sales_offer_snapshot_amounts", "\"BasePriceAmount\" > 0 AND \"LineItemsAmount\" >= 0 AND \"DiscountAmount\" >= 0 AND \"FinalPriceAmount\" > 0 AND \"CostSnapshotAmount\" >= 0 AND \"MinimumMarginAmount\" >= 0");
                table.ForeignKey(
                    name: "FK_approved_offer_snapshots_offers_OrganizationId_OfferId",
                    columns: x => new { x.OrganizationId, x.OfferId },
                    principalSchema: "sales",
                    principalTable: "offers",
                    principalColumns: ["OrganizationId", "Id"],
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_approved_offer_snapshots_users_OrganizationId_ApprovedByUse~",
                    columns: x => new { x.OrganizationId, x.ApprovedByUserId },
                    principalSchema: "identity",
                    principalTable: "users",
                    principalColumns: ["OrganizationId", "Id"],
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "offer_decisions",
            schema: "sales",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                OfferId = table.Column<Guid>(type: "uuid", nullable: false),
                Type = table.Column<int>(type: "integer", nullable: false),
                Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_offer_decisions", x => new { x.OrganizationId, x.OfferId, x.Id });
                table.CheckConstraint("ck_sales_offer_decision", "\"Type\" BETWEEN 1 AND 3");
                table.ForeignKey(
                    name: "FK_offer_decisions_offers_OrganizationId_OfferId",
                    columns: x => new { x.OrganizationId, x.OfferId },
                    principalSchema: "sales",
                    principalTable: "offers",
                    principalColumns: ["OrganizationId", "Id"],
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_offer_decisions_users_OrganizationId_ActorUserId",
                    columns: x => new { x.OrganizationId, x.ActorUserId },
                    principalSchema: "identity",
                    principalTable: "users",
                    principalColumns: ["OrganizationId", "Id"],
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "offer_history",
            schema: "sales",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                OfferId = table.Column<Guid>(type: "uuid", nullable: false),
                CommandId = table.Column<Guid>(type: "uuid", nullable: false),
                Operation = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                Signature = table.Column<string>(type: "text", nullable: true),
                ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_offer_history", x => new { x.OrganizationId, x.OfferId, x.Id });
                table.ForeignKey(
                    name: "FK_offer_history_offers_OrganizationId_OfferId",
                    columns: x => new { x.OrganizationId, x.OfferId },
                    principalSchema: "sales",
                    principalTable: "offers",
                    principalColumns: ["OrganizationId", "Id"],
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_offer_history_users_OrganizationId_ActorUserId",
                    columns: x => new { x.OrganizationId, x.ActorUserId },
                    principalSchema: "identity",
                    principalTable: "users",
                    principalColumns: ["OrganizationId", "Id"],
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "offer_line_items",
            schema: "sales",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                OfferId = table.Column<Guid>(type: "uuid", nullable: false),
                Category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                Name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                Amount = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: false),
                Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_offer_line_items", x => new { x.OrganizationId, x.OfferId, x.Id });
                table.CheckConstraint("ck_sales_offer_line_amount", "\"Amount\" > 0");
                table.ForeignKey(
                    name: "FK_offer_line_items_offers_OrganizationId_OfferId",
                    columns: x => new { x.OrganizationId, x.OfferId },
                    principalSchema: "sales",
                    principalTable: "offers",
                    principalColumns: ["OrganizationId", "Id"],
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "visit_history",
            schema: "sales",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                VisitId = table.Column<Guid>(type: "uuid", nullable: false),
                CommandId = table.Column<Guid>(type: "uuid", nullable: false),
                Operation = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                Signature = table.Column<string>(type: "text", nullable: true),
                ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_visit_history", x => new { x.OrganizationId, x.VisitId, x.Id });
                table.ForeignKey(
                    name: "FK_visit_history_users_OrganizationId_ActorUserId",
                    columns: x => new { x.OrganizationId, x.ActorUserId },
                    principalSchema: "identity",
                    principalTable: "users",
                    principalColumns: ["OrganizationId", "Id"],
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_visit_history_visits_OrganizationId_VisitId",
                    columns: x => new { x.OrganizationId, x.VisitId },
                    principalSchema: "sales",
                    principalTable: "visits",
                    principalColumns: ["OrganizationId", "Id"],
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_approved_offer_snapshots_OrganizationId_ApprovedByUserId",
            schema: "sales",
            table: "approved_offer_snapshots",
            columns: ["OrganizationId", "ApprovedByUserId"]);

        migrationBuilder.CreateIndex(
            name: "ux_sales_offer_snapshot",
            schema: "sales",
            table: "approved_offer_snapshots",
            columns: ["OrganizationId", "OfferId"],
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_offer_decisions_OrganizationId_ActorUserId",
            schema: "sales",
            table: "offer_decisions",
            columns: ["OrganizationId", "ActorUserId"]);

        migrationBuilder.CreateIndex(
            name: "IX_offer_history_OrganizationId_ActorUserId",
            schema: "sales",
            table: "offer_history",
            columns: ["OrganizationId", "ActorUserId"]);

        migrationBuilder.CreateIndex(
            name: "ux_sales_offer_history_command",
            schema: "sales",
            table: "offer_history",
            columns: ["OrganizationId", "OfferId", "CommandId"],
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_offers_OrganizationId_BranchId_Status",
            schema: "sales",
            table: "offers",
            columns: ["OrganizationId", "BranchId", "Status"]);

        migrationBuilder.CreateIndex(
            name: "IX_offers_OrganizationId_CreatedByUserId",
            schema: "sales",
            table: "offers",
            columns: ["OrganizationId", "CreatedByUserId"]);

        migrationBuilder.CreateIndex(
            name: "IX_offers_OrganizationId_CustomerId",
            schema: "sales",
            table: "offers",
            columns: ["OrganizationId", "CustomerId"]);

        migrationBuilder.CreateIndex(
            name: "IX_offers_OrganizationId_RevisesOfferId",
            schema: "sales",
            table: "offers",
            columns: ["OrganizationId", "RevisesOfferId"]);

        migrationBuilder.CreateIndex(
            name: "IX_offers_OrganizationId_VehicleId",
            schema: "sales",
            table: "offers",
            columns: ["OrganizationId", "VehicleId"]);

        migrationBuilder.CreateIndex(
            name: "ux_sales_offer_active",
            schema: "sales",
            table: "offers",
            columns: ["OrganizationId", "LeadId"],
            unique: true,
            filter: "\"Status\" IN (1, 2, 5)");

        migrationBuilder.CreateIndex(
            name: "ux_sales_offer_lead_revision",
            schema: "sales",
            table: "offers",
            columns: ["OrganizationId", "LeadId", "Revision"],
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_visit_history_OrganizationId_ActorUserId",
            schema: "sales",
            table: "visit_history",
            columns: ["OrganizationId", "ActorUserId"]);

        migrationBuilder.CreateIndex(
            name: "ux_sales_visit_history_command",
            schema: "sales",
            table: "visit_history",
            columns: ["OrganizationId", "VisitId", "CommandId"],
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_visits_OrganizationId_BranchId_StartsAt",
            schema: "sales",
            table: "visits",
            columns: ["OrganizationId", "BranchId", "StartsAt"]);

        migrationBuilder.CreateIndex(
            name: "IX_visits_OrganizationId_CreatedByUserId",
            schema: "sales",
            table: "visits",
            columns: ["OrganizationId", "CreatedByUserId"]);

        migrationBuilder.CreateIndex(
            name: "IX_visits_OrganizationId_CustomerId",
            schema: "sales",
            table: "visits",
            columns: ["OrganizationId", "CustomerId"]);

        migrationBuilder.CreateIndex(
            name: "IX_visits_OrganizationId_LeadId",
            schema: "sales",
            table: "visits",
            columns: ["OrganizationId", "LeadId"]);

        migrationBuilder.CreateIndex(
            name: "IX_visits_OrganizationId_ResponsibleUserId_StartsAt",
            schema: "sales",
            table: "visits",
            columns: ["OrganizationId", "ResponsibleUserId", "StartsAt"]);

        migrationBuilder.CreateIndex(
            name: "IX_visits_OrganizationId_VehicleId_StartsAt",
            schema: "sales",
            table: "visits",
            columns: ["OrganizationId", "VehicleId", "StartsAt"]);

        migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS btree_gist;");

        migrationBuilder.Sql(
            """
            ALTER TABLE sales.visits
                ADD CONSTRAINT ex_sales_visit_vehicle_slot
                EXCLUDE USING gist (
                    "OrganizationId" WITH =,
                    "VehicleId" WITH =,
                    tstzrange("StartsAt", "EndsAt", '[)') WITH &&)
                WHERE ("IncludesTestDrive" AND "Status" IN (1, 2));
            """);

        migrationBuilder.Sql(
            """
            ALTER TABLE sales.visits
                ADD CONSTRAINT ex_sales_visit_responsible_slot
                EXCLUDE USING gist (
                    "OrganizationId" WITH =,
                    "ResponsibleUserId" WITH =,
                    tstzrange("StartsAt", "EndsAt", '[)') WITH &&)
                WHERE ("Status" IN (1, 2));
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE sales.visits DROP CONSTRAINT IF EXISTS ex_sales_visit_responsible_slot;
            ALTER TABLE sales.visits DROP CONSTRAINT IF EXISTS ex_sales_visit_vehicle_slot;
            ALTER TABLE organizations.organizations DROP CONSTRAINT IF EXISTS ck_organizations_sales_policy;
            """);

        migrationBuilder.DropTable(
            name: "approved_offer_snapshots",
            schema: "sales");

        migrationBuilder.DropTable(
            name: "offer_decisions",
            schema: "sales");

        migrationBuilder.DropTable(
            name: "offer_history",
            schema: "sales");

        migrationBuilder.DropTable(
            name: "offer_line_items",
            schema: "sales");

        migrationBuilder.DropTable(
            name: "visit_history",
            schema: "sales");

        migrationBuilder.DropTable(
            name: "offers",
            schema: "sales");

        migrationBuilder.DropTable(
            name: "visits",
            schema: "sales");

        migrationBuilder.DropColumn(
            name: "SalesAutoApprovalDiscountLimit",
            schema: "organizations",
            table: "organizations");

        migrationBuilder.DropColumn(
            name: "SalesMinimumMarginAmount",
            schema: "organizations",
            table: "organizations");
    }
}

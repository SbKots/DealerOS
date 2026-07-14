using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DealerOS.Api.Infrastructure.Migrations;

/// <inheritdoc />
public partial class DealPaymentsDocuments09 : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "deals");

        migrationBuilder.CreateTable(
            name: "deals",
            schema: "deals",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                ReservationId = table.Column<Guid>(type: "uuid", nullable: false),
                ApprovedOfferSnapshotId = table.Column<Guid>(type: "uuid", nullable: false),
                CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                LeadId = table.Column<Guid>(type: "uuid", nullable: false),
                VehicleId = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                CreateCommandId = table.Column<Guid>(type: "uuid", nullable: false),
                CreateSignature = table.Column<string>(type: "text", nullable: false),
                CustomerNameSnapshot = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                VehicleSnapshotJson = table.Column<string>(type: "jsonb", nullable: false),
                LineItemsJson = table.Column<string>(type: "jsonb", nullable: false),
                BasePriceAmount = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: false),
                LineItemsAmount = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: false),
                DiscountAmount = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: false),
                FinalTotalAmount = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: false),
                CostSnapshotAmount = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: false),
                ExpectedMarginAmount = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: false),
                Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                Status = table.Column<int>(type: "integer", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                ClosedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                ClosureReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                Version = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_deals", x => x.Id);
                table.UniqueConstraint("ak_deals_organization_id", x => new { x.OrganizationId, x.Id });
                table.CheckConstraint("ck_deals_amounts", "\"BasePriceAmount\" > 0 AND \"LineItemsAmount\" >= 0 AND \"DiscountAmount\" >= 0 AND \"FinalTotalAmount\" > 0 AND \"CostSnapshotAmount\" >= 0");
                table.CheckConstraint("ck_deals_currency", "\"Currency\" ~ '^[A-Z]{3}$'");
                table.CheckConstraint("ck_deals_status", "\"Status\" BETWEEN 1 AND 7");
                table.CheckConstraint("ck_deals_version", "\"Version\" > 0");
                table.ForeignKey(
                    name: "FK_deals_approved_offer_snapshots_OrganizationId_ApprovedOffer~",
                    columns: x => new { x.OrganizationId, x.ApprovedOfferSnapshotId },
                    principalSchema: "sales",
                    principalTable: "approved_offer_snapshots",
                    principalColumns: new[] { "OrganizationId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_deals_branches_OrganizationId_BranchId",
                    columns: x => new { x.OrganizationId, x.BranchId },
                    principalSchema: "organizations",
                    principalTable: "branches",
                    principalColumns: new[] { "OrganizationId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_deals_customers_OrganizationId_CustomerId",
                    columns: x => new { x.OrganizationId, x.CustomerId },
                    principalSchema: "crm",
                    principalTable: "customers",
                    principalColumns: new[] { "OrganizationId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_deals_leads_OrganizationId_LeadId",
                    columns: x => new { x.OrganizationId, x.LeadId },
                    principalSchema: "crm",
                    principalTable: "leads",
                    principalColumns: new[] { "OrganizationId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_deals_reservations_OrganizationId_ReservationId",
                    columns: x => new { x.OrganizationId, x.ReservationId },
                    principalSchema: "reservations",
                    principalTable: "reservations",
                    principalColumns: new[] { "OrganizationId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_deals_users_OrganizationId_CreatedByUserId",
                    columns: x => new { x.OrganizationId, x.CreatedByUserId },
                    principalSchema: "identity",
                    principalTable: "users",
                    principalColumns: new[] { "OrganizationId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_deals_vehicles_OrganizationId_VehicleId",
                    columns: x => new { x.OrganizationId, x.VehicleId },
                    principalSchema: "vehicles",
                    principalTable: "vehicles",
                    principalColumns: new[] { "OrganizationId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "deal_history",
            schema: "deals",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                DealId = table.Column<Guid>(type: "uuid", nullable: false),
                CommandId = table.Column<Guid>(type: "uuid", nullable: false),
                Operation = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                Signature = table.Column<string>(type: "text", nullable: true),
                ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_deal_history", x => new { x.OrganizationId, x.DealId, x.Id });
                table.ForeignKey(
                    name: "FK_deal_history_deals_OrganizationId_DealId",
                    columns: x => new { x.OrganizationId, x.DealId },
                    principalSchema: "deals",
                    principalTable: "deals",
                    principalColumns: new[] { "OrganizationId", "Id" },
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_deal_history_users_OrganizationId_ActorUserId",
                    columns: x => new { x.OrganizationId, x.ActorUserId },
                    principalSchema: "identity",
                    principalTable: "users",
                    principalColumns: new[] { "OrganizationId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "documents",
            schema: "deals",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                DealId = table.Column<Guid>(type: "uuid", nullable: false),
                CommandId = table.Column<Guid>(type: "uuid", nullable: false),
                Type = table.Column<int>(type: "integer", nullable: false),
                Number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                TemplateName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                TemplateVersion = table.Column<int>(type: "integer", nullable: false),
                Revision = table.Column<int>(type: "integer", nullable: false),
                SourceDocumentId = table.Column<Guid>(type: "uuid", nullable: true),
                Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                ObjectKey = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                GeneratedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                GeneratedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_documents", x => x.Id);
                table.UniqueConstraint("ak_deal_documents_organization_id", x => new { x.OrganizationId, x.Id });
                table.CheckConstraint("ck_deal_documents_revision", "\"Revision\" > 0 AND \"TemplateVersion\" > 0");
                table.CheckConstraint("ck_deal_documents_size", "\"SizeBytes\" > 0");
                table.CheckConstraint("ck_deal_documents_type", "\"Type\" BETWEEN 1 AND 2");
                table.ForeignKey(
                    name: "FK_documents_deals_OrganizationId_DealId",
                    columns: x => new { x.OrganizationId, x.DealId },
                    principalSchema: "deals",
                    principalTable: "deals",
                    principalColumns: new[] { "OrganizationId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_documents_documents_OrganizationId_SourceDocumentId",
                    columns: x => new { x.OrganizationId, x.SourceDocumentId },
                    principalSchema: "deals",
                    principalTable: "documents",
                    principalColumns: new[] { "OrganizationId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_documents_users_OrganizationId_GeneratedByUserId",
                    columns: x => new { x.OrganizationId, x.GeneratedByUserId },
                    principalSchema: "identity",
                    principalTable: "users",
                    principalColumns: new[] { "OrganizationId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "handover_snapshots",
            schema: "deals",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                DealId = table.Column<Guid>(type: "uuid", nullable: false),
                ActualMileageKm = table.Column<int>(type: "integer", nullable: false),
                KeysTransferred = table.Column<bool>(type: "boolean", nullable: false),
                DocumentsTransferred = table.Column<bool>(type: "boolean", nullable: false),
                EquipmentTransferred = table.Column<bool>(type: "boolean", nullable: false),
                ConditionConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                IssuerConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                ResponsibleConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                ConditionNotes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                Comments = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                CompletedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_handover_snapshots", x => x.Id);
                table.CheckConstraint("ck_deal_handover_mileage", "\"ActualMileageKm\" BETWEEN 0 AND 3000000");
                table.ForeignKey(
                    name: "FK_handover_snapshots_deals_OrganizationId_DealId",
                    columns: x => new { x.OrganizationId, x.DealId },
                    principalSchema: "deals",
                    principalTable: "deals",
                    principalColumns: new[] { "OrganizationId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_handover_snapshots_users_OrganizationId_CompletedByUserId",
                    columns: x => new { x.OrganizationId, x.CompletedByUserId },
                    principalSchema: "identity",
                    principalTable: "users",
                    principalColumns: new[] { "OrganizationId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "payments",
            schema: "deals",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                DealId = table.Column<Guid>(type: "uuid", nullable: false),
                CommandId = table.Column<Guid>(type: "uuid", nullable: false),
                Kind = table.Column<int>(type: "integer", nullable: false),
                Status = table.Column<int>(type: "integer", nullable: false),
                Amount = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: false),
                Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                ManualReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_payments", x => x.Id);
                table.UniqueConstraint("ak_deal_payments_organization_id", x => new { x.OrganizationId, x.Id });
                table.CheckConstraint("ck_deal_payments_amount", "\"Amount\" > 0");
                table.CheckConstraint("ck_deal_payments_currency", "\"Currency\" ~ '^[A-Z]{3}$'");
                table.CheckConstraint("ck_deal_payments_kind", "\"Kind\" BETWEEN 1 AND 4");
                table.CheckConstraint("ck_deal_payments_status", "\"Status\" BETWEEN 1 AND 5");
                table.ForeignKey(
                    name: "FK_payments_deals_OrganizationId_DealId",
                    columns: x => new { x.OrganizationId, x.DealId },
                    principalSchema: "deals",
                    principalTable: "deals",
                    principalColumns: new[] { "OrganizationId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_payments_users_OrganizationId_ActorUserId",
                    columns: x => new { x.OrganizationId, x.ActorUserId },
                    principalSchema: "identity",
                    principalTable: "users",
                    principalColumns: new[] { "OrganizationId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_deal_history_OrganizationId_ActorUserId",
            schema: "deals",
            table: "deal_history",
            columns: new[] { "OrganizationId", "ActorUserId" });

        migrationBuilder.CreateIndex(
            name: "ux_deal_history_command",
            schema: "deals",
            table: "deal_history",
            columns: new[] { "OrganizationId", "DealId", "CommandId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_deals_OrganizationId_ApprovedOfferSnapshotId",
            schema: "deals",
            table: "deals",
            columns: new[] { "OrganizationId", "ApprovedOfferSnapshotId" });

        migrationBuilder.CreateIndex(
            name: "IX_deals_OrganizationId_CreatedByUserId",
            schema: "deals",
            table: "deals",
            columns: new[] { "OrganizationId", "CreatedByUserId" });

        migrationBuilder.CreateIndex(
            name: "IX_deals_OrganizationId_CustomerId",
            schema: "deals",
            table: "deals",
            columns: new[] { "OrganizationId", "CustomerId" });

        migrationBuilder.CreateIndex(
            name: "IX_deals_OrganizationId_LeadId",
            schema: "deals",
            table: "deals",
            columns: new[] { "OrganizationId", "LeadId" });

        migrationBuilder.CreateIndex(
            name: "ix_deals_queue",
            schema: "deals",
            table: "deals",
            columns: new[] { "OrganizationId", "BranchId", "Status", "CreatedAt" });

        migrationBuilder.CreateIndex(
            name: "ux_deals_active_completed_vehicle",
            schema: "deals",
            table: "deals",
            columns: new[] { "OrganizationId", "VehicleId" },
            unique: true,
            filter: "\"Status\" IN (1, 2, 3, 4, 6)");

        migrationBuilder.CreateIndex(
            name: "ux_deals_create_command",
            schema: "deals",
            table: "deals",
            columns: new[] { "OrganizationId", "CreateCommandId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ux_deals_reservation",
            schema: "deals",
            table: "deals",
            columns: new[] { "OrganizationId", "ReservationId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_documents_OrganizationId_GeneratedByUserId",
            schema: "deals",
            table: "documents",
            columns: new[] { "OrganizationId", "GeneratedByUserId" });

        migrationBuilder.CreateIndex(
            name: "IX_documents_OrganizationId_SourceDocumentId",
            schema: "deals",
            table: "documents",
            columns: new[] { "OrganizationId", "SourceDocumentId" });

        migrationBuilder.CreateIndex(
            name: "ux_deal_documents_command",
            schema: "deals",
            table: "documents",
            columns: new[] { "OrganizationId", "CommandId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ux_deal_documents_revision",
            schema: "deals",
            table: "documents",
            columns: new[] { "OrganizationId", "DealId", "Type", "Revision" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_handover_snapshots_OrganizationId_CompletedByUserId",
            schema: "deals",
            table: "handover_snapshots",
            columns: new[] { "OrganizationId", "CompletedByUserId" });

        migrationBuilder.CreateIndex(
            name: "ux_deal_handover",
            schema: "deals",
            table: "handover_snapshots",
            columns: new[] { "OrganizationId", "DealId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_deal_payments_timeline",
            schema: "deals",
            table: "payments",
            columns: new[] { "OrganizationId", "DealId", "OccurredAt" });

        migrationBuilder.CreateIndex(
            name: "IX_payments_OrganizationId_ActorUserId",
            schema: "deals",
            table: "payments",
            columns: new[] { "OrganizationId", "ActorUserId" });

        migrationBuilder.CreateIndex(
            name: "ux_deal_payments_command",
            schema: "deals",
            table: "payments",
            columns: new[] { "OrganizationId", "CommandId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ux_deal_payments_reference",
            schema: "deals",
            table: "payments",
            columns: new[] { "OrganizationId", "ManualReference" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "deal_history",
            schema: "deals");

        migrationBuilder.DropTable(
            name: "documents",
            schema: "deals");

        migrationBuilder.DropTable(
            name: "handover_snapshots",
            schema: "deals");

        migrationBuilder.DropTable(
            name: "payments",
            schema: "deals");

        migrationBuilder.DropTable(
            name: "deals",
            schema: "deals");
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DealerOS.Api.Infrastructure.Migrations;

/// <inheritdoc />
public partial class QualityListing05 : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_vehicles_acceptance_state",
            schema: "vehicles",
            table: "vehicles");

        migrationBuilder.DropCheckConstraint(
            name: "ck_vehicles_status",
            schema: "vehicles",
            table: "vehicles");

        migrationBuilder.DropCheckConstraint(
            name: "ck_status_history_from",
            schema: "vehicles",
            table: "status_history");

        migrationBuilder.DropCheckConstraint(
            name: "ck_status_history_to",
            schema: "vehicles",
            table: "status_history");

        migrationBuilder.CreateTable(
            name: "listing_contents",
            schema: "operations",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                VehicleId = table.Column<Guid>(type: "uuid", nullable: false),
                Revision = table.Column<int>(type: "integer", nullable: false),
                Status = table.Column<int>(type: "integer", nullable: false),
                VehicleMake = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                VehicleModel = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                VehicleYear = table.Column<int>(type: "integer", nullable: false),
                MileageKm = table.Column<int>(type: "integer", nullable: false),
                Equipment = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                Advantages = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                ConditionDescription = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                PublicPriceAmount = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: false),
                Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                TemplateName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                TemplateVersion = table.Column<int>(type: "integer", nullable: false),
                SnapshotJson = table.Column<string>(type: "jsonb", nullable: true),
                CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                ReadyAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                Version = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_listing_contents", x => x.Id);
                table.UniqueConstraint("ak_listing_organization_id", x => new { x.OrganizationId, x.Id });
                table.CheckConstraint("ck_listing_price", "\"PublicPriceAmount\" >= 0");
                table.CheckConstraint("ck_listing_revision", "\"Revision\" > 0");
                table.CheckConstraint("ck_listing_status", "\"Status\" IN (1, 2)");
                table.CheckConstraint("ck_listing_version", "\"Version\" > 0");
                table.ForeignKey(
                    name: "FK_listing_contents_branches_OrganizationId_BranchId",
                    columns: x => new { x.OrganizationId, x.BranchId },
                    principalSchema: "organizations",
                    principalTable: "branches",
                    principalColumns: new[] { "OrganizationId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_listing_contents_users_OrganizationId_CreatedByUserId",
                    columns: x => new { x.OrganizationId, x.CreatedByUserId },
                    principalSchema: "identity",
                    principalTable: "users",
                    principalColumns: new[] { "OrganizationId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_listing_contents_vehicles_OrganizationId_VehicleId",
                    columns: x => new { x.OrganizationId, x.VehicleId },
                    principalSchema: "vehicles",
                    principalTable: "vehicles",
                    principalColumns: new[] { "OrganizationId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "quality_checks",
            schema: "operations",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                VehicleId = table.Column<Guid>(type: "uuid", nullable: false),
                ExecutionId = table.Column<Guid>(type: "uuid", nullable: false),
                Revision = table.Column<int>(type: "integer", nullable: false),
                Status = table.Column<int>(type: "integer", nullable: false),
                ChecklistSnapshotJson = table.Column<string>(type: "jsonb", nullable: false),
                DecisionComment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                DecidedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                DecidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                Version = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_quality_checks", x => x.Id);
                table.UniqueConstraint("ak_quality_organization_id", x => new { x.OrganizationId, x.Id });
                table.CheckConstraint("ck_quality_revision", "\"Revision\" > 0");
                table.CheckConstraint("ck_quality_status", "\"Status\" BETWEEN 1 AND 4");
                table.CheckConstraint("ck_quality_version", "\"Version\" > 0");
                table.ForeignKey(
                    name: "FK_quality_checks_branches_OrganizationId_BranchId",
                    columns: x => new { x.OrganizationId, x.BranchId },
                    principalSchema: "organizations",
                    principalTable: "branches",
                    principalColumns: new[] { "OrganizationId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_quality_checks_executions_OrganizationId_ExecutionId",
                    columns: x => new { x.OrganizationId, x.ExecutionId },
                    principalSchema: "operations",
                    principalTable: "executions",
                    principalColumns: new[] { "OrganizationId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_quality_checks_users_OrganizationId_CreatedByUserId",
                    columns: x => new { x.OrganizationId, x.CreatedByUserId },
                    principalSchema: "identity",
                    principalTable: "users",
                    principalColumns: new[] { "OrganizationId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_quality_checks_users_OrganizationId_DecidedByUserId",
                    columns: x => new { x.OrganizationId, x.DecidedByUserId },
                    principalSchema: "identity",
                    principalTable: "users",
                    principalColumns: new[] { "OrganizationId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_quality_checks_vehicles_OrganizationId_VehicleId",
                    columns: x => new { x.OrganizationId, x.VehicleId },
                    principalSchema: "vehicles",
                    principalTable: "vehicles",
                    principalColumns: new[] { "OrganizationId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "vehicle_media",
            schema: "operations",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                VehicleId = table.Column<Guid>(type: "uuid", nullable: false),
                Category = table.Column<int>(type: "integer", nullable: false),
                ObjectKey = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: false),
                OriginalFileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                SortOrder = table.Column<int>(type: "integer", nullable: false),
                IsCover = table.Column<bool>(type: "boolean", nullable: false),
                CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                Version = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_vehicle_media", x => x.Id);
                table.UniqueConstraint("ak_vehicle_media_organization_id", x => new { x.OrganizationId, x.Id });
                table.CheckConstraint("ck_vehicle_media_category", "\"Category\" BETWEEN 1 AND 4");
                table.CheckConstraint("ck_vehicle_media_size", "\"SizeBytes\" > 0");
                table.CheckConstraint("ck_vehicle_media_sort", "\"SortOrder\" >= 0");
                table.CheckConstraint("ck_vehicle_media_version", "\"Version\" > 0");
                table.ForeignKey(
                    name: "FK_vehicle_media_branches_OrganizationId_BranchId",
                    columns: x => new { x.OrganizationId, x.BranchId },
                    principalSchema: "organizations",
                    principalTable: "branches",
                    principalColumns: new[] { "OrganizationId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_vehicle_media_users_OrganizationId_CreatedByUserId",
                    columns: x => new { x.OrganizationId, x.CreatedByUserId },
                    principalSchema: "identity",
                    principalTable: "users",
                    principalColumns: new[] { "OrganizationId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_vehicle_media_vehicles_OrganizationId_VehicleId",
                    columns: x => new { x.OrganizationId, x.VehicleId },
                    principalSchema: "vehicles",
                    principalTable: "vehicles",
                    principalColumns: new[] { "OrganizationId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "channel_publications",
            schema: "operations",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                ListingContentId = table.Column<Guid>(type: "uuid", nullable: false),
                Channel = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                Status = table.Column<int>(type: "integer", nullable: false),
                ExternalId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                ExternalUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                Error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                ExportedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                UnpublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_channel_publications", x => new { x.OrganizationId, x.ListingContentId, x.Id });
                table.CheckConstraint("ck_channel_publication_status", "\"Status\" BETWEEN 1 AND 5");
                table.ForeignKey(
                    name: "FK_channel_publications_listing_contents_OrganizationId_Listin~",
                    columns: x => new { x.OrganizationId, x.ListingContentId },
                    principalSchema: "operations",
                    principalTable: "listing_contents",
                    principalColumns: new[] { "OrganizationId", "Id" },
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "listing_history",
            schema: "operations",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                ListingContentId = table.Column<Guid>(type: "uuid", nullable: false),
                CommandId = table.Column<Guid>(type: "uuid", nullable: false),
                Action = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                Signature = table.Column<string>(type: "text", nullable: false),
                PublicPriceAmount = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: false),
                Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_listing_history", x => new { x.OrganizationId, x.ListingContentId, x.Id });
                table.ForeignKey(
                    name: "FK_listing_history_listing_contents_OrganizationId_ListingCont~",
                    columns: x => new { x.OrganizationId, x.ListingContentId },
                    principalSchema: "operations",
                    principalTable: "listing_contents",
                    principalColumns: new[] { "OrganizationId", "Id" },
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_listing_history_users_OrganizationId_ActorUserId",
                    columns: x => new { x.OrganizationId, x.ActorUserId },
                    principalSchema: "identity",
                    principalTable: "users",
                    principalColumns: new[] { "OrganizationId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "quality_observations",
            schema: "operations",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                QualityCheckId = table.Column<Guid>(type: "uuid", nullable: false),
                Severity = table.Column<int>(type: "integer", nullable: false),
                WorkOrderId = table.Column<Guid>(type: "uuid", nullable: true),
                DefectId = table.Column<Guid>(type: "uuid", nullable: true),
                Comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                RequiresRework = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_quality_observations", x => new { x.OrganizationId, x.QualityCheckId, x.Id });
                table.CheckConstraint("ck_quality_observation_severity", "\"Severity\" BETWEEN 1 AND 3");
                table.ForeignKey(
                    name: "FK_quality_observations_defects_OrganizationId_DefectId",
                    columns: x => new { x.OrganizationId, x.DefectId },
                    principalSchema: "inspections",
                    principalTable: "defects",
                    principalColumns: new[] { "OrganizationId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_quality_observations_quality_checks_OrganizationId_QualityC~",
                    columns: x => new { x.OrganizationId, x.QualityCheckId },
                    principalSchema: "operations",
                    principalTable: "quality_checks",
                    principalColumns: new[] { "OrganizationId", "Id" },
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_quality_observations_work_orders_OrganizationId_WorkOrderId",
                    columns: x => new { x.OrganizationId, x.WorkOrderId },
                    principalSchema: "operations",
                    principalTable: "work_orders",
                    principalColumns: new[] { "OrganizationId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.AddCheckConstraint(
            name: "ck_vehicles_acceptance_state",
            schema: "vehicles",
            table: "vehicles",
            sql: "(\"Status\" = 1 AND \"AcceptedAt\" IS NULL AND \"StockNumber\" IS NULL) OR (\"Status\" IN (2, 3, 4, 5, 6) AND \"AcceptedAt\" IS NOT NULL AND \"StockNumber\" IS NOT NULL)");

        migrationBuilder.AddCheckConstraint(
            name: "ck_vehicles_status",
            schema: "vehicles",
            table: "vehicles",
            sql: "\"Status\" IN (1, 2, 3, 4, 5, 6)");

        migrationBuilder.AddCheckConstraint(
            name: "ck_status_history_from",
            schema: "vehicles",
            table: "status_history",
            sql: "\"FromStatus\" IS NULL OR \"FromStatus\" IN (1, 2, 3, 4, 5, 6)");

        migrationBuilder.AddCheckConstraint(
            name: "ck_status_history_to",
            schema: "vehicles",
            table: "status_history",
            sql: "\"ToStatus\" IN (1, 2, 3, 4, 5, 6)");

        migrationBuilder.CreateIndex(
            name: "ux_channel_publication_listing_channel",
            schema: "operations",
            table: "channel_publications",
            columns: new[] { "OrganizationId", "ListingContentId", "Channel" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_listing_contents_OrganizationId_BranchId",
            schema: "operations",
            table: "listing_contents",
            columns: new[] { "OrganizationId", "BranchId" });

        migrationBuilder.CreateIndex(
            name: "IX_listing_contents_OrganizationId_CreatedByUserId",
            schema: "operations",
            table: "listing_contents",
            columns: new[] { "OrganizationId", "CreatedByUserId" });

        migrationBuilder.CreateIndex(
            name: "ux_listing_vehicle_draft",
            schema: "operations",
            table: "listing_contents",
            columns: new[] { "OrganizationId", "VehicleId" },
            unique: true,
            filter: "\"Status\" = 1");

        migrationBuilder.CreateIndex(
            name: "ux_listing_vehicle_revision",
            schema: "operations",
            table: "listing_contents",
            columns: new[] { "OrganizationId", "VehicleId", "Revision" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_listing_history_OrganizationId_ActorUserId",
            schema: "operations",
            table: "listing_history",
            columns: new[] { "OrganizationId", "ActorUserId" });

        migrationBuilder.CreateIndex(
            name: "ux_listing_history_command",
            schema: "operations",
            table: "listing_history",
            columns: new[] { "OrganizationId", "ListingContentId", "CommandId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_quality_checks_OrganizationId_BranchId",
            schema: "operations",
            table: "quality_checks",
            columns: new[] { "OrganizationId", "BranchId" });

        migrationBuilder.CreateIndex(
            name: "IX_quality_checks_OrganizationId_CreatedByUserId",
            schema: "operations",
            table: "quality_checks",
            columns: new[] { "OrganizationId", "CreatedByUserId" });

        migrationBuilder.CreateIndex(
            name: "IX_quality_checks_OrganizationId_DecidedByUserId",
            schema: "operations",
            table: "quality_checks",
            columns: new[] { "OrganizationId", "DecidedByUserId" });

        migrationBuilder.CreateIndex(
            name: "IX_quality_checks_OrganizationId_VehicleId",
            schema: "operations",
            table: "quality_checks",
            columns: new[] { "OrganizationId", "VehicleId" });

        migrationBuilder.CreateIndex(
            name: "ux_quality_execution_draft",
            schema: "operations",
            table: "quality_checks",
            columns: new[] { "OrganizationId", "ExecutionId" },
            unique: true,
            filter: "\"Status\" = 1");

        migrationBuilder.CreateIndex(
            name: "ux_quality_execution_revision",
            schema: "operations",
            table: "quality_checks",
            columns: new[] { "OrganizationId", "ExecutionId", "Revision" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_quality_observations_OrganizationId_DefectId",
            schema: "operations",
            table: "quality_observations",
            columns: new[] { "OrganizationId", "DefectId" });

        migrationBuilder.CreateIndex(
            name: "IX_quality_observations_OrganizationId_WorkOrderId",
            schema: "operations",
            table: "quality_observations",
            columns: new[] { "OrganizationId", "WorkOrderId" });

        migrationBuilder.CreateIndex(
            name: "IX_vehicle_media_OrganizationId_BranchId",
            schema: "operations",
            table: "vehicle_media",
            columns: new[] { "OrganizationId", "BranchId" });

        migrationBuilder.CreateIndex(
            name: "IX_vehicle_media_OrganizationId_CreatedByUserId",
            schema: "operations",
            table: "vehicle_media",
            columns: new[] { "OrganizationId", "CreatedByUserId" });

        migrationBuilder.CreateIndex(
            name: "IX_vehicle_media_OrganizationId_VehicleId_SortOrder",
            schema: "operations",
            table: "vehicle_media",
            columns: new[] { "OrganizationId", "VehicleId", "SortOrder" });

        migrationBuilder.CreateIndex(
            name: "ux_vehicle_media_cover",
            schema: "operations",
            table: "vehicle_media",
            columns: new[] { "OrganizationId", "VehicleId" },
            unique: true,
            filter: "\"IsCover\"");

        migrationBuilder.CreateIndex(
            name: "ux_vehicle_media_object_key",
            schema: "operations",
            table: "vehicle_media",
            column: "ObjectKey",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "channel_publications",
            schema: "operations");

        migrationBuilder.DropTable(
            name: "listing_history",
            schema: "operations");

        migrationBuilder.DropTable(
            name: "quality_observations",
            schema: "operations");

        migrationBuilder.DropTable(
            name: "vehicle_media",
            schema: "operations");

        migrationBuilder.DropTable(
            name: "listing_contents",
            schema: "operations");

        migrationBuilder.DropTable(
            name: "quality_checks",
            schema: "operations");

        migrationBuilder.DropCheckConstraint(
            name: "ck_vehicles_acceptance_state",
            schema: "vehicles",
            table: "vehicles");

        migrationBuilder.DropCheckConstraint(
            name: "ck_vehicles_status",
            schema: "vehicles",
            table: "vehicles");

        migrationBuilder.DropCheckConstraint(
            name: "ck_status_history_from",
            schema: "vehicles",
            table: "status_history");

        migrationBuilder.DropCheckConstraint(
            name: "ck_status_history_to",
            schema: "vehicles",
            table: "status_history");

        migrationBuilder.AddCheckConstraint(
            name: "ck_vehicles_acceptance_state",
            schema: "vehicles",
            table: "vehicles",
            sql: "(\"Status\" = 1 AND \"AcceptedAt\" IS NULL AND \"StockNumber\" IS NULL) OR (\"Status\" IN (2, 3, 4, 5) AND \"AcceptedAt\" IS NOT NULL AND \"StockNumber\" IS NOT NULL)");

        migrationBuilder.AddCheckConstraint(
            name: "ck_vehicles_status",
            schema: "vehicles",
            table: "vehicles",
            sql: "\"Status\" IN (1, 2, 3, 4, 5)");

        migrationBuilder.AddCheckConstraint(
            name: "ck_status_history_from",
            schema: "vehicles",
            table: "status_history",
            sql: "\"FromStatus\" IS NULL OR \"FromStatus\" IN (1, 2, 3, 4, 5)");

        migrationBuilder.AddCheckConstraint(
            name: "ck_status_history_to",
            schema: "vehicles",
            table: "status_history",
            sql: "\"ToStatus\" IN (1, 2, 3, 4, 5)");
    }
}

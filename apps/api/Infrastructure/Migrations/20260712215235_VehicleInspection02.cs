using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DealerOS.Api.Infrastructure.Migrations;

/// <inheritdoc />
public partial class VehicleInspection02 : Migration
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

        migrationBuilder.EnsureSchema(
            name: "inspections");

        migrationBuilder.CreateTable(
            name: "templates",
            schema: "inspections",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Version = table.Column<int>(type: "integer", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_templates", x => x.Id);
                table.UniqueConstraint("ak_inspection_templates_organization_id", x => new { x.OrganizationId, x.Id });
                table.CheckConstraint("ck_inspection_templates_version", "\"Version\" > 0");
                table.ForeignKey(
                    name: "FK_templates_organizations_OrganizationId",
                    column: x => x.OrganizationId,
                    principalSchema: "organizations",
                    principalTable: "organizations",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_templates_users_OrganizationId_CreatedByUserId",
                    columns: x => new { x.OrganizationId, x.CreatedByUserId },
                    principalSchema: "identity",
                    principalTable: "users",
                    principalColumns: new[] { "OrganizationId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "inspections",
            schema: "inspections",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                VehicleId = table.Column<Guid>(type: "uuid", nullable: false),
                InspectorId = table.Column<Guid>(type: "uuid", nullable: false),
                Status = table.Column<int>(type: "integer", nullable: false),
                StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                MileageKm = table.Column<int>(type: "integer", nullable: false),
                FinalComment = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                TemplateId = table.Column<Guid>(type: "uuid", nullable: false),
                TemplateName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                TemplateVersion = table.Column<int>(type: "integer", nullable: false),
                CorrectsInspectionId = table.Column<Guid>(type: "uuid", nullable: true),
                Revision = table.Column<int>(type: "integer", nullable: false),
                Version = table.Column<long>(type: "bigint", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_inspections", x => x.Id);
                table.UniqueConstraint("ak_inspections_organization_id", x => new { x.OrganizationId, x.Id });
                table.CheckConstraint("ck_inspections_mileage", "\"MileageKm\" BETWEEN 0 AND 3000000");
                table.CheckConstraint("ck_inspections_revision", "\"Revision\" > 0");
                table.CheckConstraint("ck_inspections_status", "\"Status\" IN (1, 2, 3, 4)");
                table.CheckConstraint("ck_inspections_timestamps", "(\"Status\" = 1 AND \"StartedAt\" IS NULL AND \"CompletedAt\" IS NULL) OR (\"Status\" = 2 AND \"StartedAt\" IS NOT NULL AND \"CompletedAt\" IS NULL) OR (\"Status\" = 3 AND \"StartedAt\" IS NOT NULL AND \"CompletedAt\" IS NOT NULL) OR (\"Status\" = 4 AND \"CompletedAt\" IS NULL)");
                table.CheckConstraint("ck_inspections_version", "\"Version\" > 0");
                table.ForeignKey(
                    name: "FK_inspections_branches_OrganizationId_BranchId",
                    columns: x => new { x.OrganizationId, x.BranchId },
                    principalSchema: "organizations",
                    principalTable: "branches",
                    principalColumns: new[] { "OrganizationId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_inspections_inspections_OrganizationId_CorrectsInspectionId",
                    columns: x => new { x.OrganizationId, x.CorrectsInspectionId },
                    principalSchema: "inspections",
                    principalTable: "inspections",
                    principalColumns: new[] { "OrganizationId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_inspections_templates_OrganizationId_TemplateId",
                    columns: x => new { x.OrganizationId, x.TemplateId },
                    principalSchema: "inspections",
                    principalTable: "templates",
                    principalColumns: new[] { "OrganizationId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_inspections_users_OrganizationId_InspectorId",
                    columns: x => new { x.OrganizationId, x.InspectorId },
                    principalSchema: "identity",
                    principalTable: "users",
                    principalColumns: new[] { "OrganizationId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_inspections_vehicles_OrganizationId_VehicleId",
                    columns: x => new { x.OrganizationId, x.VehicleId },
                    principalSchema: "vehicles",
                    principalTable: "vehicles",
                    principalColumns: new[] { "OrganizationId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "template_items",
            schema: "inspections",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                TemplateId = table.Column<Guid>(type: "uuid", nullable: false),
                Key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                Category = table.Column<int>(type: "integer", nullable: false),
                Label = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                IsRequired = table.Column<bool>(type: "boolean", nullable: false),
                SortOrder = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_template_items", x => x.Id);
                table.CheckConstraint("ck_inspection_template_items_category", "\"Category\" BETWEEN 1 AND 11");
                table.CheckConstraint("ck_inspection_template_items_sort", "\"SortOrder\" >= 0");
                table.ForeignKey(
                    name: "FK_template_items_templates_OrganizationId_TemplateId",
                    columns: x => new { x.OrganizationId, x.TemplateId },
                    principalSchema: "inspections",
                    principalTable: "templates",
                    principalColumns: new[] { "OrganizationId", "Id" },
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "defects",
            schema: "inspections",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                InspectionId = table.Column<Guid>(type: "uuid", nullable: false),
                Category = table.Column<int>(type: "integer", nullable: false),
                Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                Description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                Severity = table.Column<int>(type: "integer", nullable: false),
                Recommendation = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                EstimatedRepairAmount = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: true),
                Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                RepairRequired = table.Column<bool>(type: "boolean", nullable: false),
                BlocksPublication = table.Column<bool>(type: "boolean", nullable: false),
                BlocksTestDrive = table.Column<bool>(type: "boolean", nullable: false),
                BlocksSale = table.Column<bool>(type: "boolean", nullable: false),
                CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_defects", x => x.Id);
                table.UniqueConstraint("ak_inspection_defects_organization_id", x => new { x.OrganizationId, x.Id });
                table.CheckConstraint("ck_inspection_defects_amount", "\"EstimatedRepairAmount\" IS NULL OR \"EstimatedRepairAmount\" >= 0");
                table.CheckConstraint("ck_inspection_defects_category", "\"Category\" BETWEEN 1 AND 11");
                table.CheckConstraint("ck_inspection_defects_critical", "\"Severity\" <> 3 OR (\"RepairRequired\" AND \"BlocksSale\")");
                table.CheckConstraint("ck_inspection_defects_money", "(\"EstimatedRepairAmount\" IS NULL AND \"Currency\" IS NULL) OR (\"EstimatedRepairAmount\" IS NOT NULL AND \"Currency\" ~ '^[A-Z]{3}$')");
                table.CheckConstraint("ck_inspection_defects_severity", "\"Severity\" IN (1, 2, 3)");
                table.ForeignKey(
                    name: "FK_defects_inspections_OrganizationId_InspectionId",
                    columns: x => new { x.OrganizationId, x.InspectionId },
                    principalSchema: "inspections",
                    principalTable: "inspections",
                    principalColumns: new[] { "OrganizationId", "Id" },
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_defects_users_OrganizationId_CreatedByUserId",
                    columns: x => new { x.OrganizationId, x.CreatedByUserId },
                    principalSchema: "identity",
                    principalTable: "users",
                    principalColumns: new[] { "OrganizationId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "items",
            schema: "inspections",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                InspectionId = table.Column<Guid>(type: "uuid", nullable: false),
                Key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                Category = table.Column<int>(type: "integer", nullable: false),
                Label = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                IsRequired = table.Column<bool>(type: "boolean", nullable: false),
                SortOrder = table.Column<int>(type: "integer", nullable: false),
                Result = table.Column<int>(type: "integer", nullable: false),
                Comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_items", x => x.Id);
                table.CheckConstraint("ck_inspection_items_category", "\"Category\" BETWEEN 1 AND 11");
                table.CheckConstraint("ck_inspection_items_result", "\"Result\" IN (1, 2, 3, 4)");
                table.CheckConstraint("ck_inspection_items_sort", "\"SortOrder\" >= 0");
                table.ForeignKey(
                    name: "FK_items_inspections_OrganizationId_InspectionId",
                    columns: x => new { x.OrganizationId, x.InspectionId },
                    principalSchema: "inspections",
                    principalTable: "inspections",
                    principalColumns: new[] { "OrganizationId", "Id" },
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "photos",
            schema: "inspections",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                DefectId = table.Column<Guid>(type: "uuid", nullable: false),
                OriginalFileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ObjectKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                ContentType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_photos", x => x.Id);
                table.CheckConstraint("ck_inspection_photos_size", "\"SizeBytes\" > 0 AND \"SizeBytes\" <= 8388608");
                table.CheckConstraint("ck_inspection_photos_type", "\"ContentType\" IN ('image/jpeg', 'image/png', 'image/webp')");
                table.ForeignKey(
                    name: "FK_photos_defects_OrganizationId_DefectId",
                    columns: x => new { x.OrganizationId, x.DefectId },
                    principalSchema: "inspections",
                    principalTable: "defects",
                    principalColumns: new[] { "OrganizationId", "Id" },
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_photos_users_OrganizationId_CreatedByUserId",
                    columns: x => new { x.OrganizationId, x.CreatedByUserId },
                    principalSchema: "identity",
                    principalTable: "users",
                    principalColumns: new[] { "OrganizationId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });

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

        migrationBuilder.CreateIndex(
            name: "IX_defects_OrganizationId_CreatedByUserId",
            schema: "inspections",
            table: "defects",
            columns: new[] { "OrganizationId", "CreatedByUserId" });

        migrationBuilder.CreateIndex(
            name: "IX_defects_OrganizationId_InspectionId_Severity",
            schema: "inspections",
            table: "defects",
            columns: new[] { "OrganizationId", "InspectionId", "Severity" });

        migrationBuilder.CreateIndex(
            name: "IX_inspections_OrganizationId_CorrectsInspectionId",
            schema: "inspections",
            table: "inspections",
            columns: new[] { "OrganizationId", "CorrectsInspectionId" });

        migrationBuilder.CreateIndex(
            name: "IX_inspections_OrganizationId_InspectorId",
            schema: "inspections",
            table: "inspections",
            columns: new[] { "OrganizationId", "InspectorId" });

        migrationBuilder.CreateIndex(
            name: "IX_inspections_OrganizationId_TemplateId",
            schema: "inspections",
            table: "inspections",
            columns: new[] { "OrganizationId", "TemplateId" });

        migrationBuilder.CreateIndex(
            name: "ix_inspections_tenant_branch_status_created",
            schema: "inspections",
            table: "inspections",
            columns: new[] { "OrganizationId", "BranchId", "Status", "CreatedAt" });

        migrationBuilder.CreateIndex(
            name: "ux_inspections_active_vehicle",
            schema: "inspections",
            table: "inspections",
            columns: new[] { "OrganizationId", "VehicleId" },
            unique: true,
            filter: "\"Status\" IN (1, 2)");

        migrationBuilder.CreateIndex(
            name: "ux_inspection_items_snapshot_key",
            schema: "inspections",
            table: "items",
            columns: new[] { "OrganizationId", "InspectionId", "Key" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_photos_OrganizationId_CreatedByUserId",
            schema: "inspections",
            table: "photos",
            columns: new[] { "OrganizationId", "CreatedByUserId" });

        migrationBuilder.CreateIndex(
            name: "IX_photos_OrganizationId_DefectId",
            schema: "inspections",
            table: "photos",
            columns: new[] { "OrganizationId", "DefectId" });

        migrationBuilder.CreateIndex(
            name: "ux_inspection_photos_object_key",
            schema: "inspections",
            table: "photos",
            column: "ObjectKey",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ux_inspection_template_items_key",
            schema: "inspections",
            table: "template_items",
            columns: new[] { "OrganizationId", "TemplateId", "Key" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_templates_OrganizationId_CreatedByUserId",
            schema: "inspections",
            table: "templates",
            columns: new[] { "OrganizationId", "CreatedByUserId" });

        migrationBuilder.CreateIndex(
            name: "ux_inspection_templates_organization_version",
            schema: "inspections",
            table: "templates",
            columns: new[] { "OrganizationId", "Version" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "items",
            schema: "inspections");

        migrationBuilder.DropTable(
            name: "photos",
            schema: "inspections");

        migrationBuilder.DropTable(
            name: "template_items",
            schema: "inspections");

        migrationBuilder.DropTable(
            name: "defects",
            schema: "inspections");

        migrationBuilder.DropTable(
            name: "inspections",
            schema: "inspections");

        migrationBuilder.DropTable(
            name: "templates",
            schema: "inspections");

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
            sql: "(\"Status\" = 1 AND \"AcceptedAt\" IS NULL AND \"StockNumber\" IS NULL) OR (\"Status\" = 2 AND \"AcceptedAt\" IS NOT NULL AND \"StockNumber\" IS NOT NULL)");

        migrationBuilder.AddCheckConstraint(
            name: "ck_vehicles_status",
            schema: "vehicles",
            table: "vehicles",
            sql: "\"Status\" IN (1, 2)");

        migrationBuilder.AddCheckConstraint(
            name: "ck_status_history_from",
            schema: "vehicles",
            table: "status_history",
            sql: "\"FromStatus\" IS NULL OR \"FromStatus\" IN (1, 2)");

        migrationBuilder.AddCheckConstraint(
            name: "ck_status_history_to",
            schema: "vehicles",
            table: "status_history",
            sql: "\"ToStatus\" IN (1, 2)");
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DealerOS.Api.Infrastructure.Migrations;

/// <inheritdoc />
public partial class ReconditioningPlan03 : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "reconditioning");

        migrationBuilder.AddColumn<bool>(
            name: "RequireIndependentReconditioningApproval",
            schema: "organizations",
            table: "organizations",
            type: "boolean",
            nullable: false,
            defaultValue: true);

        migrationBuilder.CreateTable(
            name: "plans",
            schema: "reconditioning",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                VehicleId = table.Column<Guid>(type: "uuid", nullable: false),
                SourceInspectionId = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                Status = table.Column<int>(type: "integer", nullable: false),
                Revision = table.Column<int>(type: "integer", nullable: false),
                RevisesPlanId = table.Column<Guid>(type: "uuid", nullable: true),
                SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                DecidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                CancelledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                Version = table.Column<long>(type: "bigint", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_plans", x => x.Id);
                table.UniqueConstraint("ak_reconditioning_plans_organization_id", x => new { x.OrganizationId, x.Id });
                table.CheckConstraint("ck_reconditioning_plans_revision", "\"Revision\" > 0");
                table.CheckConstraint("ck_reconditioning_plans_status", "\"Status\" IN (1, 2, 3, 4, 5, 6)");
                table.CheckConstraint("ck_reconditioning_plans_timestamps", "(\"Status\" IN (1, 3) AND \"DecidedAt\" IS NULL AND \"CancelledAt\" IS NULL) OR (\"Status\" = 2 AND \"SubmittedAt\" IS NOT NULL AND \"DecidedAt\" IS NULL AND \"CancelledAt\" IS NULL) OR (\"Status\" IN (4, 5) AND \"SubmittedAt\" IS NOT NULL AND \"DecidedAt\" IS NOT NULL AND \"CancelledAt\" IS NULL) OR (\"Status\" = 6 AND \"CancelledAt\" IS NOT NULL)");
                table.CheckConstraint("ck_reconditioning_plans_version", "\"Version\" > 0");
                table.ForeignKey(
                    name: "FK_plans_branches_OrganizationId_BranchId",
                    columns: x => new { x.OrganizationId, x.BranchId },
                    principalSchema: "organizations",
                    principalTable: "branches",
                    principalColumns: new[] { "OrganizationId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_plans_inspections_OrganizationId_SourceInspectionId",
                    columns: x => new { x.OrganizationId, x.SourceInspectionId },
                    principalSchema: "inspections",
                    principalTable: "inspections",
                    principalColumns: new[] { "OrganizationId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_plans_plans_OrganizationId_RevisesPlanId",
                    columns: x => new { x.OrganizationId, x.RevisesPlanId },
                    principalSchema: "reconditioning",
                    principalTable: "plans",
                    principalColumns: new[] { "OrganizationId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_plans_users_OrganizationId_CreatedByUserId",
                    columns: x => new { x.OrganizationId, x.CreatedByUserId },
                    principalSchema: "identity",
                    principalTable: "users",
                    principalColumns: new[] { "OrganizationId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_plans_vehicles_OrganizationId_VehicleId",
                    columns: x => new { x.OrganizationId, x.VehicleId },
                    principalSchema: "vehicles",
                    principalTable: "vehicles",
                    principalColumns: new[] { "OrganizationId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "budget_snapshots",
            schema: "reconditioning",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                PlanId = table.Column<Guid>(type: "uuid", nullable: false),
                LaborAmount = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: false),
                PartsAmount = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: false),
                PlannedTotalAmount = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: false),
                ApprovedLimitAmount = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: false),
                Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                ApprovedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                ApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_budget_snapshots", x => x.Id);
                table.CheckConstraint("ck_reconditioning_snapshots_amounts", "\"LaborAmount\" >= 0 AND \"PartsAmount\" >= 0 AND \"PlannedTotalAmount\" >= 0 AND \"ApprovedLimitAmount\" >= 0");
                table.CheckConstraint("ck_reconditioning_snapshots_currency", "\"Currency\" ~ '^[A-Z]{3}$'");
                table.ForeignKey(
                    name: "FK_budget_snapshots_plans_OrganizationId_PlanId",
                    columns: x => new { x.OrganizationId, x.PlanId },
                    principalSchema: "reconditioning",
                    principalTable: "plans",
                    principalColumns: new[] { "OrganizationId", "Id" },
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_budget_snapshots_users_OrganizationId_ApprovedByUserId",
                    columns: x => new { x.OrganizationId, x.ApprovedByUserId },
                    principalSchema: "identity",
                    principalTable: "users",
                    principalColumns: new[] { "OrganizationId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "decisions",
            schema: "reconditioning",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                PlanId = table.Column<Guid>(type: "uuid", nullable: false),
                Type = table.Column<int>(type: "integer", nullable: false),
                ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                ApprovedLimitAmount = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: true),
                Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                DecidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_decisions", x => new { x.OrganizationId, x.PlanId, x.Id });
                table.CheckConstraint("ck_reconditioning_decisions_money", "(\"ApprovedLimitAmount\" IS NULL AND \"Currency\" IS NULL) OR (\"ApprovedLimitAmount\" >= 0 AND \"Currency\" ~ '^[A-Z]{3}$')");
                table.CheckConstraint("ck_reconditioning_decisions_type", "\"Type\" IN (1, 2, 3)");
                table.ForeignKey(
                    name: "FK_decisions_plans_OrganizationId_PlanId",
                    columns: x => new { x.OrganizationId, x.PlanId },
                    principalSchema: "reconditioning",
                    principalTable: "plans",
                    principalColumns: new[] { "OrganizationId", "Id" },
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_decisions_users_OrganizationId_ActorUserId",
                    columns: x => new { x.OrganizationId, x.ActorUserId },
                    principalSchema: "identity",
                    principalTable: "users",
                    principalColumns: new[] { "OrganizationId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "defect_omissions",
            schema: "reconditioning",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                PlanId = table.Column<Guid>(type: "uuid", nullable: false),
                SourceDefectId = table.Column<Guid>(type: "uuid", nullable: false),
                SourceDefectTitle = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                DecidedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                DecidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_defect_omissions", x => x.Id);
                table.ForeignKey(
                    name: "FK_defect_omissions_defects_OrganizationId_SourceDefectId",
                    columns: x => new { x.OrganizationId, x.SourceDefectId },
                    principalSchema: "inspections",
                    principalTable: "defects",
                    principalColumns: new[] { "OrganizationId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_defect_omissions_plans_OrganizationId_PlanId",
                    columns: x => new { x.OrganizationId, x.PlanId },
                    principalSchema: "reconditioning",
                    principalTable: "plans",
                    principalColumns: new[] { "OrganizationId", "Id" },
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_defect_omissions_users_OrganizationId_DecidedByUserId",
                    columns: x => new { x.OrganizationId, x.DecidedByUserId },
                    principalSchema: "identity",
                    principalTable: "users",
                    principalColumns: new[] { "OrganizationId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "plan_history",
            schema: "reconditioning",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                PlanId = table.Column<Guid>(type: "uuid", nullable: false),
                FromStatus = table.Column<int>(type: "integer", nullable: true),
                ToStatus = table.Column<int>(type: "integer", nullable: false),
                ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                DecisionId = table.Column<Guid>(type: "uuid", nullable: true),
                OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_plan_history", x => x.Id);
                table.CheckConstraint("ck_reconditioning_history_from", "\"FromStatus\" IS NULL OR \"FromStatus\" IN (1, 2, 3, 4, 5, 6)");
                table.CheckConstraint("ck_reconditioning_history_to", "\"ToStatus\" IN (1, 2, 3, 4, 5, 6)");
                table.ForeignKey(
                    name: "FK_plan_history_plans_OrganizationId_PlanId",
                    columns: x => new { x.OrganizationId, x.PlanId },
                    principalSchema: "reconditioning",
                    principalTable: "plans",
                    principalColumns: new[] { "OrganizationId", "Id" },
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_plan_history_users_OrganizationId_ActorUserId",
                    columns: x => new { x.OrganizationId, x.ActorUserId },
                    principalSchema: "identity",
                    principalTable: "users",
                    principalColumns: new[] { "OrganizationId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "works",
            schema: "reconditioning",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                PlanId = table.Column<Guid>(type: "uuid", nullable: false),
                SourceDefectId = table.Column<Guid>(type: "uuid", nullable: false),
                SourceDefectTitle = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                SourceDefectDescription = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                SourceDefectSeverity = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                Description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                Category = table.Column<int>(type: "integer", nullable: false),
                Priority = table.Column<int>(type: "integer", nullable: false),
                IsMandatory = table.Column<bool>(type: "boolean", nullable: false),
                ExecutorType = table.Column<int>(type: "integer", nullable: false),
                ExecutorName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                EstimatedLaborAmount = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: false),
                EstimatedPartsAmount = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: false),
                Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                EstimatedDurationDays = table.Column<int>(type: "integer", nullable: false),
                Comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_works", x => x.Id);
                table.CheckConstraint("ck_reconditioning_works_amounts", "\"EstimatedLaborAmount\" >= 0 AND \"EstimatedPartsAmount\" >= 0");
                table.CheckConstraint("ck_reconditioning_works_category", "\"Category\" BETWEEN 1 AND 8");
                table.CheckConstraint("ck_reconditioning_works_currency", "\"Currency\" ~ '^[A-Z]{3}$'");
                table.CheckConstraint("ck_reconditioning_works_duration", "\"EstimatedDurationDays\" BETWEEN 1 AND 365");
                table.CheckConstraint("ck_reconditioning_works_executor", "\"ExecutorType\" IN (1, 2)");
                table.CheckConstraint("ck_reconditioning_works_priority", "\"Priority\" BETWEEN 1 AND 4");
                table.ForeignKey(
                    name: "FK_works_defects_OrganizationId_SourceDefectId",
                    columns: x => new { x.OrganizationId, x.SourceDefectId },
                    principalSchema: "inspections",
                    principalTable: "defects",
                    principalColumns: new[] { "OrganizationId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_works_plans_OrganizationId_PlanId",
                    columns: x => new { x.OrganizationId, x.PlanId },
                    principalSchema: "reconditioning",
                    principalTable: "plans",
                    principalColumns: new[] { "OrganizationId", "Id" },
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_budget_snapshots_OrganizationId_ApprovedByUserId",
            schema: "reconditioning",
            table: "budget_snapshots",
            columns: new[] { "OrganizationId", "ApprovedByUserId" });

        migrationBuilder.CreateIndex(
            name: "ux_reconditioning_snapshot_plan",
            schema: "reconditioning",
            table: "budget_snapshots",
            columns: new[] { "OrganizationId", "PlanId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_decisions_OrganizationId_ActorUserId",
            schema: "reconditioning",
            table: "decisions",
            columns: new[] { "OrganizationId", "ActorUserId" });

        migrationBuilder.CreateIndex(
            name: "ux_reconditioning_decisions_command",
            schema: "reconditioning",
            table: "decisions",
            columns: new[] { "OrganizationId", "PlanId", "Id" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_defect_omissions_OrganizationId_DecidedByUserId",
            schema: "reconditioning",
            table: "defect_omissions",
            columns: new[] { "OrganizationId", "DecidedByUserId" });

        migrationBuilder.CreateIndex(
            name: "IX_defect_omissions_OrganizationId_SourceDefectId",
            schema: "reconditioning",
            table: "defect_omissions",
            columns: new[] { "OrganizationId", "SourceDefectId" });

        migrationBuilder.CreateIndex(
            name: "ux_reconditioning_omissions_plan_defect",
            schema: "reconditioning",
            table: "defect_omissions",
            columns: new[] { "OrganizationId", "PlanId", "SourceDefectId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_plan_history_OrganizationId_ActorUserId",
            schema: "reconditioning",
            table: "plan_history",
            columns: new[] { "OrganizationId", "ActorUserId" });

        migrationBuilder.CreateIndex(
            name: "IX_plan_history_OrganizationId_PlanId_OccurredAt",
            schema: "reconditioning",
            table: "plan_history",
            columns: new[] { "OrganizationId", "PlanId", "OccurredAt" });

        migrationBuilder.CreateIndex(
            name: "IX_plans_OrganizationId_CreatedByUserId",
            schema: "reconditioning",
            table: "plans",
            columns: new[] { "OrganizationId", "CreatedByUserId" });

        migrationBuilder.CreateIndex(
            name: "IX_plans_OrganizationId_RevisesPlanId",
            schema: "reconditioning",
            table: "plans",
            columns: new[] { "OrganizationId", "RevisesPlanId" });

        migrationBuilder.CreateIndex(
            name: "IX_plans_OrganizationId_SourceInspectionId",
            schema: "reconditioning",
            table: "plans",
            columns: new[] { "OrganizationId", "SourceInspectionId" });

        migrationBuilder.CreateIndex(
            name: "ix_reconditioning_plans_tenant_branch_status",
            schema: "reconditioning",
            table: "plans",
            columns: new[] { "OrganizationId", "BranchId", "Status", "UpdatedAt" });

        migrationBuilder.CreateIndex(
            name: "ux_reconditioning_plans_active_vehicle",
            schema: "reconditioning",
            table: "plans",
            columns: new[] { "OrganizationId", "VehicleId" },
            unique: true,
            filter: "\"Status\" IN (1, 2, 3)");

        migrationBuilder.CreateIndex(
            name: "ux_reconditioning_plans_vehicle_revision",
            schema: "reconditioning",
            table: "plans",
            columns: new[] { "OrganizationId", "VehicleId", "Revision" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_works_OrganizationId_PlanId_SourceDefectId",
            schema: "reconditioning",
            table: "works",
            columns: new[] { "OrganizationId", "PlanId", "SourceDefectId" });

        migrationBuilder.CreateIndex(
            name: "IX_works_OrganizationId_SourceDefectId",
            schema: "reconditioning",
            table: "works",
            columns: new[] { "OrganizationId", "SourceDefectId" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "budget_snapshots",
            schema: "reconditioning");

        migrationBuilder.DropTable(
            name: "decisions",
            schema: "reconditioning");

        migrationBuilder.DropTable(
            name: "defect_omissions",
            schema: "reconditioning");

        migrationBuilder.DropTable(
            name: "plan_history",
            schema: "reconditioning");

        migrationBuilder.DropTable(
            name: "works",
            schema: "reconditioning");

        migrationBuilder.DropTable(
            name: "plans",
            schema: "reconditioning");

        migrationBuilder.DropColumn(
            name: "RequireIndependentReconditioningApproval",
            schema: "organizations",
            table: "organizations");
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DealerOS.Api.Infrastructure.Migrations;

/// <inheritdoc />
public partial class OperationsExecution04 : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "operations");

        migrationBuilder.AddUniqueConstraint(
            name: "ak_reconditioning_works_organization_id",
            schema: "reconditioning",
            table: "works",
            columns: ["OrganizationId", "Id"]);

        migrationBuilder.AddUniqueConstraint(
            name: "ak_reconditioning_budget_snapshots_organization_id",
            schema: "reconditioning",
            table: "budget_snapshots",
            columns: ["OrganizationId", "Id"]);

        migrationBuilder.CreateTable(
            name: "executions",
            schema: "operations",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                VehicleId = table.Column<Guid>(type: "uuid", nullable: false),
                PlanId = table.Column<Guid>(type: "uuid", nullable: false),
                BudgetSnapshotId = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                Status = table.Column<int>(type: "integer", nullable: false),
                PlannedAmount = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: false),
                ApprovedLimitAmount = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: false),
                Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                Version = table.Column<long>(type: "bigint", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_executions", x => x.Id);
                table.UniqueConstraint("ak_operations_executions_organization_id", x => new { x.OrganizationId, x.Id });
                table.CheckConstraint("ck_operations_executions_amounts", "\"PlannedAmount\" >= 0 AND \"ApprovedLimitAmount\" >= 0");
                table.CheckConstraint("ck_operations_executions_currency", "\"Currency\" ~ '^[A-Z]{3}$'");
                table.CheckConstraint("ck_operations_executions_status", "\"Status\" IN (1, 2, 3, 4, 5)");
                table.CheckConstraint("ck_operations_executions_timestamps", "(\"Status\" = 1 AND \"StartedAt\" IS NULL AND \"CompletedAt\" IS NULL) OR (\"Status\" IN (2, 3) AND \"StartedAt\" IS NOT NULL AND \"CompletedAt\" IS NULL) OR (\"Status\" = 4 AND \"StartedAt\" IS NOT NULL AND \"CompletedAt\" IS NOT NULL) OR (\"Status\" = 5 AND \"CompletedAt\" IS NULL)");
                table.CheckConstraint("ck_operations_executions_version", "\"Version\" > 0");
                table.ForeignKey(
                    name: "FK_executions_branches_OrganizationId_BranchId",
                    columns: x => new { x.OrganizationId, x.BranchId },
                    principalSchema: "organizations",
                    principalTable: "branches",
                    principalColumns: ["OrganizationId", "Id"],
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_executions_budget_snapshots_OrganizationId_BudgetSnapshotId",
                    columns: x => new { x.OrganizationId, x.BudgetSnapshotId },
                    principalSchema: "reconditioning",
                    principalTable: "budget_snapshots",
                    principalColumns: ["OrganizationId", "Id"],
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_executions_plans_OrganizationId_PlanId",
                    columns: x => new { x.OrganizationId, x.PlanId },
                    principalSchema: "reconditioning",
                    principalTable: "plans",
                    principalColumns: ["OrganizationId", "Id"],
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_executions_users_OrganizationId_CreatedByUserId",
                    columns: x => new { x.OrganizationId, x.CreatedByUserId },
                    principalSchema: "identity",
                    principalTable: "users",
                    principalColumns: ["OrganizationId", "Id"],
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_executions_vehicles_OrganizationId_VehicleId",
                    columns: x => new { x.OrganizationId, x.VehicleId },
                    principalSchema: "vehicles",
                    principalTable: "vehicles",
                    principalColumns: ["OrganizationId", "Id"],
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "overrun_decisions",
            schema: "operations",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                ExecutionId = table.Column<Guid>(type: "uuid", nullable: false),
                ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                ActualAmount = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: false),
                ApprovedLimitAmount = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: false),
                Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                DecidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_overrun_decisions", x => new { x.OrganizationId, x.ExecutionId, x.Id });
                table.CheckConstraint("ck_operations_overrun_amounts", "\"ActualAmount\" >= 0 AND \"ApprovedLimitAmount\" >= \"ActualAmount\"");
                table.CheckConstraint("ck_operations_overrun_currency", "\"Currency\" ~ '^[A-Z]{3}$'");
                table.ForeignKey(
                    name: "FK_overrun_decisions_executions_OrganizationId_ExecutionId",
                    columns: x => new { x.OrganizationId, x.ExecutionId },
                    principalSchema: "operations",
                    principalTable: "executions",
                    principalColumns: ["OrganizationId", "Id"],
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_overrun_decisions_users_OrganizationId_ActorUserId",
                    columns: x => new { x.OrganizationId, x.ActorUserId },
                    principalSchema: "identity",
                    principalTable: "users",
                    principalColumns: ["OrganizationId", "Id"],
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "work_orders",
            schema: "operations",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                ExecutionId = table.Column<Guid>(type: "uuid", nullable: false),
                SourcePlanWorkId = table.Column<Guid>(type: "uuid", nullable: false),
                SourceDefectId = table.Column<Guid>(type: "uuid", nullable: false),
                Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                IsMandatory = table.Column<bool>(type: "boolean", nullable: false),
                ExecutorType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                AssigneeName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                DueAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                Status = table.Column<int>(type: "integer", nullable: false),
                PlannedLaborAmount = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: false),
                PlannedPartsAmount = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: false),
                ActualLaborHours = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: false),
                ActualLaborAmount = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: false),
                ActualExternalAmount = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: false),
                Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                ContractorName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                InvoiceReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                ContractorDueAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                SettlementStatus = table.Column<int>(type: "integer", nullable: false),
                SettlementChangedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                SettlementChangedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                SettlementComment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                BlockReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                CompletionComment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_work_orders", x => x.Id);
                table.UniqueConstraint("ak_operations_work_orders_organization_id", x => new { x.OrganizationId, x.Id });
                table.CheckConstraint("ck_operations_work_orders_amounts", "\"PlannedLaborAmount\" >= 0 AND \"PlannedPartsAmount\" >= 0 AND \"ActualLaborHours\" >= 0 AND \"ActualLaborAmount\" >= 0 AND \"ActualExternalAmount\" >= 0");
                table.CheckConstraint("ck_operations_work_orders_currency", "\"Currency\" ~ '^[A-Z]{3}$'");
                table.CheckConstraint("ck_operations_work_orders_settlement", "\"SettlementStatus\" BETWEEN 1 AND 6");
                table.CheckConstraint("ck_operations_work_orders_status", "\"Status\" BETWEEN 1 AND 6");
                table.ForeignKey(
                    name: "FK_work_orders_defects_OrganizationId_SourceDefectId",
                    columns: x => new { x.OrganizationId, x.SourceDefectId },
                    principalSchema: "inspections",
                    principalTable: "defects",
                    principalColumns: ["OrganizationId", "Id"],
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_work_orders_executions_OrganizationId_ExecutionId",
                    columns: x => new { x.OrganizationId, x.ExecutionId },
                    principalSchema: "operations",
                    principalTable: "executions",
                    principalColumns: ["OrganizationId", "Id"],
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_work_orders_users_OrganizationId_SettlementChangedByUserId",
                    columns: x => new { x.OrganizationId, x.SettlementChangedByUserId },
                    principalSchema: "identity",
                    principalTable: "users",
                    principalColumns: ["OrganizationId", "Id"],
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_work_orders_works_OrganizationId_SourcePlanWorkId",
                    columns: x => new { x.OrganizationId, x.SourcePlanWorkId },
                    principalSchema: "reconditioning",
                    principalTable: "works",
                    principalColumns: ["OrganizationId", "Id"],
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "material_movements",
            schema: "operations",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                WorkOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                ExecutionId = table.Column<Guid>(type: "uuid", nullable: false),
                Type = table.Column<int>(type: "integer", nullable: false),
                Name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                Quantity = table.Column<decimal>(type: "numeric(19,3)", precision: 19, scale: 3, nullable: false),
                Unit = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                UnitCost = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: false),
                Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                SupplierName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_material_movements", x => new { x.OrganizationId, x.WorkOrderId, x.Id });
                table.CheckConstraint("ck_operations_material_cost", "\"UnitCost\" >= 0");
                table.CheckConstraint("ck_operations_material_currency", "\"Currency\" ~ '^[A-Z]{3}$'");
                table.CheckConstraint("ck_operations_material_quantity", "\"Quantity\" > 0");
                table.CheckConstraint("ck_operations_material_type", "\"Type\" IN (1, 2)");
                table.ForeignKey(
                    name: "FK_material_movements_users_OrganizationId_ActorUserId",
                    columns: x => new { x.OrganizationId, x.ActorUserId },
                    principalSchema: "identity",
                    principalTable: "users",
                    principalColumns: ["OrganizationId", "Id"],
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_material_movements_work_orders_OrganizationId_WorkOrderId",
                    columns: x => new { x.OrganizationId, x.WorkOrderId },
                    principalSchema: "operations",
                    principalTable: "work_orders",
                    principalColumns: ["OrganizationId", "Id"],
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "notifications",
            schema: "operations",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                ExecutionId = table.Column<Guid>(type: "uuid", nullable: false),
                WorkOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                Type = table.Column<int>(type: "integer", nullable: false),
                DeduplicationKey = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_notifications", x => x.Id);
                table.CheckConstraint("ck_operations_notification_type", "\"Type\" IN (1, 2)");
                table.ForeignKey(
                    name: "FK_notifications_executions_OrganizationId_ExecutionId",
                    columns: x => new { x.OrganizationId, x.ExecutionId },
                    principalSchema: "operations",
                    principalTable: "executions",
                    principalColumns: ["OrganizationId", "Id"],
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_notifications_work_orders_OrganizationId_WorkOrderId",
                    columns: x => new { x.OrganizationId, x.WorkOrderId },
                    principalSchema: "operations",
                    principalTable: "work_orders",
                    principalColumns: ["OrganizationId", "Id"],
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_executions_OrganizationId_BranchId",
            schema: "operations",
            table: "executions",
            columns: ["OrganizationId", "BranchId"]);

        migrationBuilder.CreateIndex(
            name: "IX_executions_OrganizationId_CreatedByUserId",
            schema: "operations",
            table: "executions",
            columns: ["OrganizationId", "CreatedByUserId"]);

        migrationBuilder.CreateIndex(
            name: "IX_executions_OrganizationId_PlanId",
            schema: "operations",
            table: "executions",
            columns: ["OrganizationId", "PlanId"]);

        migrationBuilder.CreateIndex(
            name: "ix_operations_execution_vehicle_status",
            schema: "operations",
            table: "executions",
            columns: ["OrganizationId", "VehicleId", "Status", "UpdatedAt"]);

        migrationBuilder.CreateIndex(
            name: "ux_operations_execution_snapshot",
            schema: "operations",
            table: "executions",
            columns: ["OrganizationId", "BudgetSnapshotId"],
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_material_movements_OrganizationId_ActorUserId",
            schema: "operations",
            table: "material_movements",
            columns: ["OrganizationId", "ActorUserId"]);

        migrationBuilder.CreateIndex(
            name: "IX_notifications_OrganizationId_ExecutionId",
            schema: "operations",
            table: "notifications",
            columns: ["OrganizationId", "ExecutionId"]);

        migrationBuilder.CreateIndex(
            name: "IX_notifications_OrganizationId_WorkOrderId",
            schema: "operations",
            table: "notifications",
            columns: ["OrganizationId", "WorkOrderId"]);

        migrationBuilder.CreateIndex(
            name: "ux_operations_notification_deduplication",
            schema: "operations",
            table: "notifications",
            columns: ["OrganizationId", "DeduplicationKey"],
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_overrun_decisions_OrganizationId_ActorUserId",
            schema: "operations",
            table: "overrun_decisions",
            columns: ["OrganizationId", "ActorUserId"]);

        migrationBuilder.CreateIndex(
            name: "ix_operations_work_order_due_status",
            schema: "operations",
            table: "work_orders",
            columns: ["OrganizationId", "DueAt", "Status"]);

        migrationBuilder.CreateIndex(
            name: "IX_work_orders_OrganizationId_SettlementChangedByUserId",
            schema: "operations",
            table: "work_orders",
            columns: ["OrganizationId", "SettlementChangedByUserId"]);

        migrationBuilder.CreateIndex(
            name: "IX_work_orders_OrganizationId_SourceDefectId",
            schema: "operations",
            table: "work_orders",
            columns: ["OrganizationId", "SourceDefectId"]);

        migrationBuilder.CreateIndex(
            name: "IX_work_orders_OrganizationId_SourcePlanWorkId",
            schema: "operations",
            table: "work_orders",
            columns: ["OrganizationId", "SourcePlanWorkId"]);

        migrationBuilder.CreateIndex(
            name: "ux_operations_work_order_plan_work",
            schema: "operations",
            table: "work_orders",
            columns: ["OrganizationId", "ExecutionId", "SourcePlanWorkId"],
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "material_movements",
            schema: "operations");

        migrationBuilder.DropTable(
            name: "notifications",
            schema: "operations");

        migrationBuilder.DropTable(
            name: "overrun_decisions",
            schema: "operations");

        migrationBuilder.DropTable(
            name: "work_orders",
            schema: "operations");

        migrationBuilder.DropTable(
            name: "executions",
            schema: "operations");

        migrationBuilder.DropUniqueConstraint(
            name: "ak_reconditioning_works_organization_id",
            schema: "reconditioning",
            table: "works");

        migrationBuilder.DropUniqueConstraint(
            name: "ak_reconditioning_budget_snapshots_organization_id",
            schema: "reconditioning",
            table: "budget_snapshots");
    }
}

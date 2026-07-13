using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DealerOS.Api.Infrastructure.Migrations;

/// <inheritdoc />
public partial class TenantIntegrityAndValidation : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_status_history_vehicles_VehicleId",
            schema: "vehicles",
            table: "status_history");

        migrationBuilder.DropForeignKey(
            name: "FK_user_branch_access_users_UserId",
            schema: "identity",
            table: "user_branch_access");

        migrationBuilder.DropPrimaryKey(
            name: "PK_user_branch_access",
            schema: "identity",
            table: "user_branch_access");

        migrationBuilder.DropIndex(
            name: "IX_status_history_VehicleId",
            schema: "vehicles",
            table: "status_history");

        migrationBuilder.AddColumn<Guid>(
            name: "OrganizationId",
            schema: "identity",
            table: "user_branch_access",
            type: "uuid",
            nullable: false,
            defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

        migrationBuilder.Sql(
            """
            UPDATE identity.user_branch_access AS access
            SET "OrganizationId" = users."OrganizationId"
            FROM identity.users AS users
            WHERE users."Id" = access."UserId";
            """);

        migrationBuilder.AddUniqueConstraint(
            name: "ak_vehicles_organization_id",
            schema: "vehicles",
            table: "vehicles",
            columns: new[] { "OrganizationId", "Id" });

        migrationBuilder.AddUniqueConstraint(
            name: "ak_users_organization_id",
            schema: "identity",
            table: "users",
            columns: new[] { "OrganizationId", "Id" });

        migrationBuilder.AddPrimaryKey(
            name: "PK_user_branch_access",
            schema: "identity",
            table: "user_branch_access",
            columns: new[] { "OrganizationId", "UserId", "BranchId" });

        migrationBuilder.AddUniqueConstraint(
            name: "ak_branches_organization_id",
            schema: "organizations",
            table: "branches",
            columns: new[] { "OrganizationId", "Id" });

        migrationBuilder.CreateIndex(
            name: "IX_vehicles_OrganizationId_CreatedByUserId",
            schema: "vehicles",
            table: "vehicles",
            columns: new[] { "OrganizationId", "CreatedByUserId" });

        migrationBuilder.CreateIndex(
            name: "ix_vehicles_tenant_branch_created",
            schema: "vehicles",
            table: "vehicles",
            columns: new[] { "OrganizationId", "BranchId", "CreatedAt" });

        migrationBuilder.AddCheckConstraint(
            name: "ck_vehicles_acceptance_state",
            schema: "vehicles",
            table: "vehicles",
            sql: "(\"Status\" = 1 AND \"AcceptedAt\" IS NULL AND \"StockNumber\" IS NULL) OR (\"Status\" = 2 AND \"AcceptedAt\" IS NOT NULL AND \"StockNumber\" IS NOT NULL)");

        migrationBuilder.AddCheckConstraint(
            name: "ck_vehicles_currency",
            schema: "vehicles",
            table: "vehicles",
            sql: "\"Currency\" ~ '^[A-Z]{3}$'");

        migrationBuilder.AddCheckConstraint(
            name: "ck_vehicles_mileage",
            schema: "vehicles",
            table: "vehicles",
            sql: "\"MileageKm\" >= 0 AND \"MileageKm\" <= 3000000");

        migrationBuilder.AddCheckConstraint(
            name: "ck_vehicles_purchase_amount",
            schema: "vehicles",
            table: "vehicles",
            sql: "\"PlannedPurchaseAmount\" > 0");

        migrationBuilder.AddCheckConstraint(
            name: "ck_vehicles_status",
            schema: "vehicles",
            table: "vehicles",
            sql: "\"Status\" IN (1, 2)");

        migrationBuilder.AddCheckConstraint(
            name: "ck_vehicles_version",
            schema: "vehicles",
            table: "vehicles",
            sql: "\"Version\" > 0");

        migrationBuilder.AddCheckConstraint(
            name: "ck_vehicles_vin",
            schema: "vehicles",
            table: "vehicles",
            sql: "\"Vin\" ~ '^[A-HJ-NPR-Z0-9]{17}$'");

        migrationBuilder.AddCheckConstraint(
            name: "ck_vehicles_year",
            schema: "vehicles",
            table: "vehicles",
            sql: "\"Year\" >= 1950 AND \"Year\" <= 9999");

        migrationBuilder.CreateIndex(
            name: "IX_user_branch_access_OrganizationId_BranchId",
            schema: "identity",
            table: "user_branch_access",
            columns: new[] { "OrganizationId", "BranchId" });

        migrationBuilder.CreateIndex(
            name: "IX_status_history_OrganizationId_ChangedByUserId",
            schema: "vehicles",
            table: "status_history",
            columns: new[] { "OrganizationId", "ChangedByUserId" });

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

        migrationBuilder.CreateIndex(
            name: "IX_events_OrganizationId_ActorUserId",
            schema: "audit",
            table: "events",
            columns: new[] { "OrganizationId", "ActorUserId" });

        migrationBuilder.AddForeignKey(
            name: "FK_branches_organizations_OrganizationId",
            schema: "organizations",
            table: "branches",
            column: "OrganizationId",
            principalSchema: "organizations",
            principalTable: "organizations",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_events_organizations_OrganizationId",
            schema: "audit",
            table: "events",
            column: "OrganizationId",
            principalSchema: "organizations",
            principalTable: "organizations",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_events_users_OrganizationId_ActorUserId",
            schema: "audit",
            table: "events",
            columns: new[] { "OrganizationId", "ActorUserId" },
            principalSchema: "identity",
            principalTable: "users",
            principalColumns: new[] { "OrganizationId", "Id" },
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_status_history_users_OrganizationId_ChangedByUserId",
            schema: "vehicles",
            table: "status_history",
            columns: new[] { "OrganizationId", "ChangedByUserId" },
            principalSchema: "identity",
            principalTable: "users",
            principalColumns: new[] { "OrganizationId", "Id" },
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_status_history_vehicles_OrganizationId_VehicleId",
            schema: "vehicles",
            table: "status_history",
            columns: new[] { "OrganizationId", "VehicleId" },
            principalSchema: "vehicles",
            principalTable: "vehicles",
            principalColumns: new[] { "OrganizationId", "Id" },
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_user_branch_access_branches_OrganizationId_BranchId",
            schema: "identity",
            table: "user_branch_access",
            columns: new[] { "OrganizationId", "BranchId" },
            principalSchema: "organizations",
            principalTable: "branches",
            principalColumns: new[] { "OrganizationId", "Id" },
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_user_branch_access_users_OrganizationId_UserId",
            schema: "identity",
            table: "user_branch_access",
            columns: new[] { "OrganizationId", "UserId" },
            principalSchema: "identity",
            principalTable: "users",
            principalColumns: new[] { "OrganizationId", "Id" },
            onDelete: ReferentialAction.Cascade);

        migrationBuilder.AddForeignKey(
            name: "FK_users_organizations_OrganizationId",
            schema: "identity",
            table: "users",
            column: "OrganizationId",
            principalSchema: "organizations",
            principalTable: "organizations",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_vehicles_branches_OrganizationId_BranchId",
            schema: "vehicles",
            table: "vehicles",
            columns: new[] { "OrganizationId", "BranchId" },
            principalSchema: "organizations",
            principalTable: "branches",
            principalColumns: new[] { "OrganizationId", "Id" },
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_vehicles_organizations_OrganizationId",
            schema: "vehicles",
            table: "vehicles",
            column: "OrganizationId",
            principalSchema: "organizations",
            principalTable: "organizations",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_vehicles_users_OrganizationId_CreatedByUserId",
            schema: "vehicles",
            table: "vehicles",
            columns: new[] { "OrganizationId", "CreatedByUserId" },
            principalSchema: "identity",
            principalTable: "users",
            principalColumns: new[] { "OrganizationId", "Id" },
            onDelete: ReferentialAction.Restrict);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_branches_organizations_OrganizationId",
            schema: "organizations",
            table: "branches");

        migrationBuilder.DropForeignKey(
            name: "FK_events_organizations_OrganizationId",
            schema: "audit",
            table: "events");

        migrationBuilder.DropForeignKey(
            name: "FK_events_users_OrganizationId_ActorUserId",
            schema: "audit",
            table: "events");

        migrationBuilder.DropForeignKey(
            name: "FK_status_history_users_OrganizationId_ChangedByUserId",
            schema: "vehicles",
            table: "status_history");

        migrationBuilder.DropForeignKey(
            name: "FK_status_history_vehicles_OrganizationId_VehicleId",
            schema: "vehicles",
            table: "status_history");

        migrationBuilder.DropForeignKey(
            name: "FK_user_branch_access_branches_OrganizationId_BranchId",
            schema: "identity",
            table: "user_branch_access");

        migrationBuilder.DropForeignKey(
            name: "FK_user_branch_access_users_OrganizationId_UserId",
            schema: "identity",
            table: "user_branch_access");

        migrationBuilder.DropForeignKey(
            name: "FK_users_organizations_OrganizationId",
            schema: "identity",
            table: "users");

        migrationBuilder.DropForeignKey(
            name: "FK_vehicles_branches_OrganizationId_BranchId",
            schema: "vehicles",
            table: "vehicles");

        migrationBuilder.DropForeignKey(
            name: "FK_vehicles_organizations_OrganizationId",
            schema: "vehicles",
            table: "vehicles");

        migrationBuilder.DropForeignKey(
            name: "FK_vehicles_users_OrganizationId_CreatedByUserId",
            schema: "vehicles",
            table: "vehicles");

        migrationBuilder.DropUniqueConstraint(
            name: "ak_vehicles_organization_id",
            schema: "vehicles",
            table: "vehicles");

        migrationBuilder.DropIndex(
            name: "IX_vehicles_OrganizationId_CreatedByUserId",
            schema: "vehicles",
            table: "vehicles");

        migrationBuilder.DropIndex(
            name: "ix_vehicles_tenant_branch_created",
            schema: "vehicles",
            table: "vehicles");

        migrationBuilder.DropCheckConstraint(
            name: "ck_vehicles_acceptance_state",
            schema: "vehicles",
            table: "vehicles");

        migrationBuilder.DropCheckConstraint(
            name: "ck_vehicles_currency",
            schema: "vehicles",
            table: "vehicles");

        migrationBuilder.DropCheckConstraint(
            name: "ck_vehicles_mileage",
            schema: "vehicles",
            table: "vehicles");

        migrationBuilder.DropCheckConstraint(
            name: "ck_vehicles_purchase_amount",
            schema: "vehicles",
            table: "vehicles");

        migrationBuilder.DropCheckConstraint(
            name: "ck_vehicles_status",
            schema: "vehicles",
            table: "vehicles");

        migrationBuilder.DropCheckConstraint(
            name: "ck_vehicles_version",
            schema: "vehicles",
            table: "vehicles");

        migrationBuilder.DropCheckConstraint(
            name: "ck_vehicles_vin",
            schema: "vehicles",
            table: "vehicles");

        migrationBuilder.DropCheckConstraint(
            name: "ck_vehicles_year",
            schema: "vehicles",
            table: "vehicles");

        migrationBuilder.DropUniqueConstraint(
            name: "ak_users_organization_id",
            schema: "identity",
            table: "users");

        migrationBuilder.DropPrimaryKey(
            name: "PK_user_branch_access",
            schema: "identity",
            table: "user_branch_access");

        migrationBuilder.DropIndex(
            name: "IX_user_branch_access_OrganizationId_BranchId",
            schema: "identity",
            table: "user_branch_access");

        migrationBuilder.DropIndex(
            name: "IX_status_history_OrganizationId_ChangedByUserId",
            schema: "vehicles",
            table: "status_history");

        migrationBuilder.DropCheckConstraint(
            name: "ck_status_history_from",
            schema: "vehicles",
            table: "status_history");

        migrationBuilder.DropCheckConstraint(
            name: "ck_status_history_to",
            schema: "vehicles",
            table: "status_history");

        migrationBuilder.DropIndex(
            name: "IX_events_OrganizationId_ActorUserId",
            schema: "audit",
            table: "events");

        migrationBuilder.DropUniqueConstraint(
            name: "ak_branches_organization_id",
            schema: "organizations",
            table: "branches");

        migrationBuilder.DropColumn(
            name: "OrganizationId",
            schema: "identity",
            table: "user_branch_access");

        migrationBuilder.AddPrimaryKey(
            name: "PK_user_branch_access",
            schema: "identity",
            table: "user_branch_access",
            columns: new[] { "UserId", "BranchId" });

        migrationBuilder.CreateIndex(
            name: "IX_status_history_VehicleId",
            schema: "vehicles",
            table: "status_history",
            column: "VehicleId");

        migrationBuilder.AddForeignKey(
            name: "FK_status_history_vehicles_VehicleId",
            schema: "vehicles",
            table: "status_history",
            column: "VehicleId",
            principalSchema: "vehicles",
            principalTable: "vehicles",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_user_branch_access_users_UserId",
            schema: "identity",
            table: "user_branch_access",
            column: "UserId",
            principalSchema: "identity",
            principalTable: "users",
            principalColumn: "Id",
            onDelete: ReferentialAction.Cascade);
    }
}

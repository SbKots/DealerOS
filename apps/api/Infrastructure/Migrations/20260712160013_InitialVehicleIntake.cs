using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DealerOS.Api.Infrastructure.Migrations;

/// <inheritdoc />
public partial class InitialVehicleIntake : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "organizations");

        migrationBuilder.EnsureSchema(
            name: "audit");

        migrationBuilder.EnsureSchema(
            name: "vehicles");

        migrationBuilder.EnsureSchema(
            name: "identity");

        migrationBuilder.CreateTable(
            name: "branches",
            schema: "organizations",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                Code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_branches", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "events",
            schema: "audit",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                Operation = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                EntityType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                EntityId = table.Column<Guid>(type: "uuid", nullable: false),
                OldValue = table.Column<string>(type: "jsonb", nullable: true),
                NewValue = table.Column<string>(type: "jsonb", nullable: false),
                CorrelationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_events", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "organizations",
            schema: "organizations",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_organizations", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "users",
            schema: "identity",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                PasswordHash = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                Permissions = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_users", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "vehicles",
            schema: "vehicles",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                Vin = table.Column<string>(type: "character varying(17)", maxLength: 17, nullable: false),
                Make = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                Model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                Year = table.Column<int>(type: "integer", nullable: false),
                MileageKm = table.Column<int>(type: "integer", nullable: false),
                PlannedPurchaseAmount = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: false),
                Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                Status = table.Column<int>(type: "integer", nullable: false),
                StockNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                AcceptedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                Version = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_vehicles", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "user_branch_access",
            schema: "identity",
            columns: table => new
            {
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                BranchId = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_user_branch_access", x => new { x.UserId, x.BranchId });
                table.ForeignKey(
                    name: "FK_user_branch_access_users_UserId",
                    column: x => x.UserId,
                    principalSchema: "identity",
                    principalTable: "users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "status_history",
            schema: "vehicles",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                VehicleId = table.Column<Guid>(type: "uuid", nullable: false),
                OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                FromStatus = table.Column<int>(type: "integer", nullable: true),
                ToStatus = table.Column<int>(type: "integer", nullable: false),
                ChangedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                ChangedByUserId = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_status_history", x => x.Id);
                table.ForeignKey(
                    name: "FK_status_history_vehicles_VehicleId",
                    column: x => x.VehicleId,
                    principalSchema: "vehicles",
                    principalTable: "vehicles",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "ux_branches_organization_code",
            schema: "organizations",
            table: "branches",
            columns: new[] { "OrganizationId", "Code" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_events_OrganizationId_EntityType_EntityId_OccurredAt",
            schema: "audit",
            table: "events",
            columns: new[] { "OrganizationId", "EntityType", "EntityId", "OccurredAt" });

        migrationBuilder.CreateIndex(
            name: "IX_status_history_OrganizationId_VehicleId_ChangedAt",
            schema: "vehicles",
            table: "status_history",
            columns: new[] { "OrganizationId", "VehicleId", "ChangedAt" });

        migrationBuilder.CreateIndex(
            name: "IX_status_history_VehicleId",
            schema: "vehicles",
            table: "status_history",
            column: "VehicleId");

        migrationBuilder.CreateIndex(
            name: "ux_users_organization_email",
            schema: "identity",
            table: "users",
            columns: new[] { "OrganizationId", "Email" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ux_vehicles_organization_stock_number",
            schema: "vehicles",
            table: "vehicles",
            columns: new[] { "OrganizationId", "StockNumber" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ux_vehicles_organization_vin",
            schema: "vehicles",
            table: "vehicles",
            columns: new[] { "OrganizationId", "Vin" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "branches",
            schema: "organizations");

        migrationBuilder.DropTable(
            name: "events",
            schema: "audit");

        migrationBuilder.DropTable(
            name: "organizations",
            schema: "organizations");

        migrationBuilder.DropTable(
            name: "status_history",
            schema: "vehicles");

        migrationBuilder.DropTable(
            name: "user_branch_access",
            schema: "identity");

        migrationBuilder.DropTable(
            name: "vehicles",
            schema: "vehicles");

        migrationBuilder.DropTable(
            name: "users",
            schema: "identity");
    }
}

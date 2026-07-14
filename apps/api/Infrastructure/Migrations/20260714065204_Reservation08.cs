using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DealerOS.Api.Infrastructure.Migrations;

/// <inheritdoc />
public partial class Reservation08 : Migration
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
            name: "reservations");

        migrationBuilder.AddUniqueConstraint(
            name: "ak_sales_offer_snapshots_organization_id",
            schema: "sales",
            table: "approved_offer_snapshots",
            columns: new[] { "OrganizationId", "Id" });

        migrationBuilder.CreateTable(
            name: "reservations",
            schema: "reservations",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                VehicleId = table.Column<Guid>(type: "uuid", nullable: false),
                CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                LeadId = table.Column<Guid>(type: "uuid", nullable: false),
                ApprovedOfferSnapshotId = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                CreateCommandId = table.Column<Guid>(type: "uuid", nullable: false),
                CreateSignature = table.Column<string>(type: "text", nullable: false),
                Status = table.Column<int>(type: "integer", nullable: false),
                DepositStatus = table.Column<int>(type: "integer", nullable: false),
                DepositAmount = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: false),
                Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                DepositReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                StartsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                ClosedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                ClosureReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                Version = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_reservations", x => x.Id);
                table.UniqueConstraint("ak_reservations_organization_id", x => new { x.OrganizationId, x.Id });
                table.CheckConstraint("ck_reservations_currency", "\"Currency\" ~ '^[A-Z]{3}$'");
                table.CheckConstraint("ck_reservations_deposit_amount", "\"DepositAmount\" >= 0");
                table.CheckConstraint("ck_reservations_deposit_status", "\"DepositStatus\" BETWEEN 1 AND 6");
                table.CheckConstraint("ck_reservations_expiration", "\"ExpiresAt\" > \"StartsAt\"");
                table.CheckConstraint("ck_reservations_status", "\"Status\" BETWEEN 1 AND 6");
                table.CheckConstraint("ck_reservations_version", "\"Version\" > 0");
                table.ForeignKey(
                    name: "FK_reservations_approved_offer_snapshots_OrganizationId_Approv~",
                    columns: x => new { x.OrganizationId, x.ApprovedOfferSnapshotId },
                    principalSchema: "sales",
                    principalTable: "approved_offer_snapshots",
                    principalColumns: new[] { "OrganizationId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_reservations_branches_OrganizationId_BranchId",
                    columns: x => new { x.OrganizationId, x.BranchId },
                    principalSchema: "organizations",
                    principalTable: "branches",
                    principalColumns: new[] { "OrganizationId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_reservations_customers_OrganizationId_CustomerId",
                    columns: x => new { x.OrganizationId, x.CustomerId },
                    principalSchema: "crm",
                    principalTable: "customers",
                    principalColumns: new[] { "OrganizationId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_reservations_leads_OrganizationId_LeadId",
                    columns: x => new { x.OrganizationId, x.LeadId },
                    principalSchema: "crm",
                    principalTable: "leads",
                    principalColumns: new[] { "OrganizationId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_reservations_users_OrganizationId_CreatedByUserId",
                    columns: x => new { x.OrganizationId, x.CreatedByUserId },
                    principalSchema: "identity",
                    principalTable: "users",
                    principalColumns: new[] { "OrganizationId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_reservations_vehicles_OrganizationId_VehicleId",
                    columns: x => new { x.OrganizationId, x.VehicleId },
                    principalSchema: "vehicles",
                    principalTable: "vehicles",
                    principalColumns: new[] { "OrganizationId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "reservation_history",
            schema: "reservations",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                ReservationId = table.Column<Guid>(type: "uuid", nullable: false),
                CommandId = table.Column<Guid>(type: "uuid", nullable: false),
                Operation = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                Signature = table.Column<string>(type: "text", nullable: true),
                ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_reservation_history", x => new { x.OrganizationId, x.ReservationId, x.Id });
                table.ForeignKey(
                    name: "FK_reservation_history_reservations_OrganizationId_Reservation~",
                    columns: x => new { x.OrganizationId, x.ReservationId },
                    principalSchema: "reservations",
                    principalTable: "reservations",
                    principalColumns: new[] { "OrganizationId", "Id" },
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_reservation_history_users_OrganizationId_ActorUserId",
                    columns: x => new { x.OrganizationId, x.ActorUserId },
                    principalSchema: "identity",
                    principalTable: "users",
                    principalColumns: new[] { "OrganizationId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.AddCheckConstraint(
            name: "ck_vehicles_acceptance_state",
            schema: "vehicles",
            table: "vehicles",
            sql: "(\"Status\" = 1 AND \"AcceptedAt\" IS NULL AND \"StockNumber\" IS NULL) OR (\"Status\" BETWEEN 2 AND 9 AND \"AcceptedAt\" IS NOT NULL AND \"StockNumber\" IS NOT NULL)");

        migrationBuilder.AddCheckConstraint(
            name: "ck_vehicles_status",
            schema: "vehicles",
            table: "vehicles",
            sql: "\"Status\" BETWEEN 1 AND 9");

        migrationBuilder.AddCheckConstraint(
            name: "ck_status_history_from",
            schema: "vehicles",
            table: "status_history",
            sql: "\"FromStatus\" IS NULL OR \"FromStatus\" BETWEEN 1 AND 9");

        migrationBuilder.AddCheckConstraint(
            name: "ck_status_history_to",
            schema: "vehicles",
            table: "status_history",
            sql: "\"ToStatus\" BETWEEN 1 AND 9");

        migrationBuilder.CreateIndex(
            name: "IX_reservation_history_OrganizationId_ActorUserId",
            schema: "reservations",
            table: "reservation_history",
            columns: new[] { "OrganizationId", "ActorUserId" });

        migrationBuilder.CreateIndex(
            name: "ux_reservation_history_command",
            schema: "reservations",
            table: "reservation_history",
            columns: new[] { "OrganizationId", "ReservationId", "CommandId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_reservations_OrganizationId_ApprovedOfferSnapshotId",
            schema: "reservations",
            table: "reservations",
            columns: new[] { "OrganizationId", "ApprovedOfferSnapshotId" });

        migrationBuilder.CreateIndex(
            name: "IX_reservations_OrganizationId_CreatedByUserId",
            schema: "reservations",
            table: "reservations",
            columns: new[] { "OrganizationId", "CreatedByUserId" });

        migrationBuilder.CreateIndex(
            name: "IX_reservations_OrganizationId_CustomerId",
            schema: "reservations",
            table: "reservations",
            columns: new[] { "OrganizationId", "CustomerId" });

        migrationBuilder.CreateIndex(
            name: "IX_reservations_OrganizationId_LeadId",
            schema: "reservations",
            table: "reservations",
            columns: new[] { "OrganizationId", "LeadId" });

        migrationBuilder.CreateIndex(
            name: "ix_reservations_queue",
            schema: "reservations",
            table: "reservations",
            columns: new[] { "OrganizationId", "BranchId", "Status", "ExpiresAt" });

        migrationBuilder.CreateIndex(
            name: "ux_reservations_active_vehicle",
            schema: "reservations",
            table: "reservations",
            columns: new[] { "OrganizationId", "VehicleId" },
            unique: true,
            filter: "\"Status\" IN (1, 2)");

        migrationBuilder.CreateIndex(
            name: "ux_reservations_create_command",
            schema: "reservations",
            table: "reservations",
            columns: new[] { "OrganizationId", "CreateCommandId" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "reservation_history",
            schema: "reservations");

        migrationBuilder.DropTable(
            name: "reservations",
            schema: "reservations");

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

        migrationBuilder.DropUniqueConstraint(
            name: "ak_sales_offer_snapshots_organization_id",
            schema: "sales",
            table: "approved_offer_snapshots");

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
    }
}

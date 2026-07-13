using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DealerOS.Api.Infrastructure.Migrations;

/// <inheritdoc />
public partial class CustomerLead06 : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "crm");

        migrationBuilder.AddColumn<int>(
            name: "LeadFirstResponseSlaMinutes",
            schema: "organizations",
            table: "organizations",
            type: "integer",
            nullable: false,
            defaultValue: 30);

        migrationBuilder.CreateTable(
            name: "customers",
            schema: "crm",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedInBranchId = table.Column<Guid>(type: "uuid", nullable: false),
                Type = table.Column<int>(type: "integer", nullable: false),
                Name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                NormalizedPhone = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                NormalizedEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                PreferredChannel = table.Column<int>(type: "integer", nullable: false),
                ConsentGiven = table.Column<bool>(type: "boolean", nullable: false),
                MarketingConsent = table.Column<bool>(type: "boolean", nullable: false),
                ConsentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                ConsentSource = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                MergedIntoCustomerId = table.Column<Guid>(type: "uuid", nullable: true),
                MergedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                MergeCommandId = table.Column<Guid>(type: "uuid", nullable: true),
                MergeReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                MergedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                Version = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_customers", x => x.Id);
                table.UniqueConstraint("ak_crm_customers_organization_id", x => new { x.OrganizationId, x.Id });
                table.CheckConstraint("ck_crm_customer_channel", "\"PreferredChannel\" BETWEEN 1 AND 3");
                table.CheckConstraint("ck_crm_customer_consent", "(NOT \"ConsentGiven\" AND NOT \"MarketingConsent\") OR (\"ConsentAt\" IS NOT NULL AND \"ConsentSource\" IS NOT NULL)");
                table.CheckConstraint("ck_crm_customer_type", "\"Type\" IN (1, 2)");
                table.CheckConstraint("ck_crm_customer_version", "\"Version\" > 0");
                table.ForeignKey(
                    name: "FK_customers_branches_OrganizationId_CreatedInBranchId",
                    columns: x => new { x.OrganizationId, x.CreatedInBranchId },
                    principalSchema: "organizations",
                    principalTable: "branches",
                    principalColumns: new[] { "OrganizationId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_customers_customers_OrganizationId_MergedIntoCustomerId",
                    columns: x => new { x.OrganizationId, x.MergedIntoCustomerId },
                    principalSchema: "crm",
                    principalTable: "customers",
                    principalColumns: new[] { "OrganizationId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_customers_users_OrganizationId_CreatedByUserId",
                    columns: x => new { x.OrganizationId, x.CreatedByUserId },
                    principalSchema: "identity",
                    principalTable: "users",
                    principalColumns: new[] { "OrganizationId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_customers_users_OrganizationId_MergedByUserId",
                    columns: x => new { x.OrganizationId, x.MergedByUserId },
                    principalSchema: "identity",
                    principalTable: "users",
                    principalColumns: new[] { "OrganizationId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "leads",
            schema: "crm",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                VehicleId = table.Column<Guid>(type: "uuid", nullable: true),
                SearchCriteria = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                Source = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                AssignedManagerUserId = table.Column<Guid>(type: "uuid", nullable: true),
                Status = table.Column<int>(type: "integer", nullable: false),
                CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                AssignedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                FirstResponseDueAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                FirstResponseAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                LostReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                NextAction = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                NextActionDueAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                Version = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_leads", x => x.Id);
                table.UniqueConstraint("ak_crm_leads_organization_id", x => new { x.OrganizationId, x.Id });
                table.CheckConstraint("ck_crm_lead_first_response", "\"FirstResponseAt\" IS NULL OR \"AssignedManagerUserId\" IS NOT NULL");
                table.CheckConstraint("ck_crm_lead_interest", "\"VehicleId\" IS NOT NULL OR \"SearchCriteria\" IS NOT NULL");
                table.CheckConstraint("ck_crm_lead_status", "\"Status\" BETWEEN 1 AND 8");
                table.CheckConstraint("ck_crm_lead_version", "\"Version\" > 0");
                table.ForeignKey(
                    name: "FK_leads_branches_OrganizationId_BranchId",
                    columns: x => new { x.OrganizationId, x.BranchId },
                    principalSchema: "organizations",
                    principalTable: "branches",
                    principalColumns: new[] { "OrganizationId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_leads_customers_OrganizationId_CustomerId",
                    columns: x => new { x.OrganizationId, x.CustomerId },
                    principalSchema: "crm",
                    principalTable: "customers",
                    principalColumns: new[] { "OrganizationId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_leads_users_OrganizationId_AssignedManagerUserId",
                    columns: x => new { x.OrganizationId, x.AssignedManagerUserId },
                    principalSchema: "identity",
                    principalTable: "users",
                    principalColumns: new[] { "OrganizationId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_leads_users_OrganizationId_CreatedByUserId",
                    columns: x => new { x.OrganizationId, x.CreatedByUserId },
                    principalSchema: "identity",
                    principalTable: "users",
                    principalColumns: new[] { "OrganizationId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_leads_vehicles_OrganizationId_VehicleId",
                    columns: x => new { x.OrganizationId, x.VehicleId },
                    principalSchema: "vehicles",
                    principalTable: "vehicles",
                    principalColumns: new[] { "OrganizationId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "lead_activities",
            schema: "crm",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                LeadId = table.Column<Guid>(type: "uuid", nullable: false),
                CommandId = table.Column<Guid>(type: "uuid", nullable: false),
                Type = table.Column<int>(type: "integer", nullable: false),
                Direction = table.Column<int>(type: "integer", nullable: false),
                Result = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                Summary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                IsClosed = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_lead_activities", x => new { x.OrganizationId, x.LeadId, x.Id });
                table.CheckConstraint("ck_crm_activity_direction", "\"Direction\" BETWEEN 1 AND 3");
                table.CheckConstraint("ck_crm_activity_type", "\"Type\" BETWEEN 1 AND 5");
                table.ForeignKey(
                    name: "FK_lead_activities_leads_OrganizationId_LeadId",
                    columns: x => new { x.OrganizationId, x.LeadId },
                    principalSchema: "crm",
                    principalTable: "leads",
                    principalColumns: new[] { "OrganizationId", "Id" },
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_lead_activities_users_OrganizationId_ActorUserId",
                    columns: x => new { x.OrganizationId, x.ActorUserId },
                    principalSchema: "identity",
                    principalTable: "users",
                    principalColumns: new[] { "OrganizationId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "lead_history",
            schema: "crm",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                LeadId = table.Column<Guid>(type: "uuid", nullable: false),
                CommandId = table.Column<Guid>(type: "uuid", nullable: false),
                FromStatus = table.Column<int>(type: "integer", nullable: true),
                ToStatus = table.Column<int>(type: "integer", nullable: false),
                Operation = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                Signature = table.Column<string>(type: "text", nullable: true),
                ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_lead_history", x => new { x.OrganizationId, x.LeadId, x.Id });
                table.CheckConstraint("ck_crm_history_from", "\"FromStatus\" IS NULL OR \"FromStatus\" BETWEEN 1 AND 8");
                table.CheckConstraint("ck_crm_history_to", "\"ToStatus\" BETWEEN 1 AND 8");
                table.ForeignKey(
                    name: "FK_lead_history_leads_OrganizationId_LeadId",
                    columns: x => new { x.OrganizationId, x.LeadId },
                    principalSchema: "crm",
                    principalTable: "leads",
                    principalColumns: new[] { "OrganizationId", "Id" },
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_lead_history_users_OrganizationId_ActorUserId",
                    columns: x => new { x.OrganizationId, x.ActorUserId },
                    principalSchema: "identity",
                    principalTable: "users",
                    principalColumns: new[] { "OrganizationId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_customers_OrganizationId_CreatedByUserId",
            schema: "crm",
            table: "customers",
            columns: new[] { "OrganizationId", "CreatedByUserId" });

        migrationBuilder.CreateIndex(
            name: "IX_customers_OrganizationId_CreatedInBranchId",
            schema: "crm",
            table: "customers",
            columns: new[] { "OrganizationId", "CreatedInBranchId" });

        migrationBuilder.CreateIndex(
            name: "IX_customers_OrganizationId_MergedByUserId",
            schema: "crm",
            table: "customers",
            columns: new[] { "OrganizationId", "MergedByUserId" });

        migrationBuilder.CreateIndex(
            name: "IX_customers_OrganizationId_MergedIntoCustomerId",
            schema: "crm",
            table: "customers",
            columns: new[] { "OrganizationId", "MergedIntoCustomerId" });

        migrationBuilder.CreateIndex(
            name: "IX_customers_OrganizationId_Name",
            schema: "crm",
            table: "customers",
            columns: new[] { "OrganizationId", "Name" });

        migrationBuilder.CreateIndex(
            name: "IX_customers_OrganizationId_NormalizedEmail",
            schema: "crm",
            table: "customers",
            columns: new[] { "OrganizationId", "NormalizedEmail" });

        migrationBuilder.CreateIndex(
            name: "IX_customers_OrganizationId_NormalizedPhone",
            schema: "crm",
            table: "customers",
            columns: new[] { "OrganizationId", "NormalizedPhone" });

        migrationBuilder.CreateIndex(
            name: "IX_lead_activities_OrganizationId_ActorUserId",
            schema: "crm",
            table: "lead_activities",
            columns: new[] { "OrganizationId", "ActorUserId" });

        migrationBuilder.CreateIndex(
            name: "ux_crm_activity_command",
            schema: "crm",
            table: "lead_activities",
            columns: new[] { "OrganizationId", "LeadId", "CommandId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_lead_history_OrganizationId_ActorUserId",
            schema: "crm",
            table: "lead_history",
            columns: new[] { "OrganizationId", "ActorUserId" });

        migrationBuilder.CreateIndex(
            name: "ux_crm_lead_history_command",
            schema: "crm",
            table: "lead_history",
            columns: new[] { "OrganizationId", "LeadId", "CommandId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_leads_OrganizationId_AssignedManagerUserId_Status",
            schema: "crm",
            table: "leads",
            columns: new[] { "OrganizationId", "AssignedManagerUserId", "Status" });

        migrationBuilder.CreateIndex(
            name: "IX_leads_OrganizationId_BranchId_Status_CreatedAt",
            schema: "crm",
            table: "leads",
            columns: new[] { "OrganizationId", "BranchId", "Status", "CreatedAt" });

        migrationBuilder.CreateIndex(
            name: "IX_leads_OrganizationId_CreatedByUserId",
            schema: "crm",
            table: "leads",
            columns: new[] { "OrganizationId", "CreatedByUserId" });

        migrationBuilder.CreateIndex(
            name: "IX_leads_OrganizationId_CustomerId",
            schema: "crm",
            table: "leads",
            columns: new[] { "OrganizationId", "CustomerId" });

        migrationBuilder.CreateIndex(
            name: "IX_leads_OrganizationId_FirstResponseDueAt",
            schema: "crm",
            table: "leads",
            columns: new[] { "OrganizationId", "FirstResponseDueAt" });

        migrationBuilder.CreateIndex(
            name: "IX_leads_OrganizationId_VehicleId",
            schema: "crm",
            table: "leads",
            columns: new[] { "OrganizationId", "VehicleId" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "lead_activities",
            schema: "crm");

        migrationBuilder.DropTable(
            name: "lead_history",
            schema: "crm");

        migrationBuilder.DropTable(
            name: "leads",
            schema: "crm");

        migrationBuilder.DropTable(
            name: "customers",
            schema: "crm");

        migrationBuilder.DropColumn(
            name: "LeadFirstResponseSlaMinutes",
            schema: "organizations",
            table: "organizations");
    }
}

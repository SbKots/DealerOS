using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DealerOS.Api.Infrastructure.Migrations;

/// <inheritdoc />
public partial class InspectionPhotoReliability02 : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ux_inspection_photos_object_key",
            schema: "inspections",
            table: "photos");

        migrationBuilder.AddColumn<Guid>(
            name: "SourcePhotoId",
            schema: "inspections",
            table: "photos",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddUniqueConstraint(
            name: "ak_inspection_photos_organization_id",
            schema: "inspections",
            table: "photos",
            columns: ["OrganizationId", "Id"]);

        migrationBuilder.CreateTable(
            name: "object_deletion_queue",
            schema: "inspections",
            columns: table => new
            {
                OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                ObjectKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_object_deletion_queue", x => new { x.OrganizationId, x.ObjectKey });
                table.ForeignKey(
                    name: "FK_object_deletion_queue_organizations_OrganizationId",
                    column: x => x.OrganizationId,
                    principalSchema: "organizations",
                    principalTable: "organizations",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "ix_inspection_photos_tenant_object_key",
            schema: "inspections",
            table: "photos",
            columns: ["OrganizationId", "ObjectKey"]);

        migrationBuilder.CreateIndex(
            name: "IX_photos_OrganizationId_SourcePhotoId",
            schema: "inspections",
            table: "photos",
            columns: ["OrganizationId", "SourcePhotoId"]);

        migrationBuilder.AddForeignKey(
            name: "FK_photos_photos_OrganizationId_SourcePhotoId",
            schema: "inspections",
            table: "photos",
            columns: ["OrganizationId", "SourcePhotoId"],
            principalSchema: "inspections",
            principalTable: "photos",
            principalColumns: ["OrganizationId", "Id"],
            onDelete: ReferentialAction.Restrict);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_photos_photos_OrganizationId_SourcePhotoId",
            schema: "inspections",
            table: "photos");

        migrationBuilder.DropTable(
            name: "object_deletion_queue",
            schema: "inspections");

        migrationBuilder.DropUniqueConstraint(
            name: "ak_inspection_photos_organization_id",
            schema: "inspections",
            table: "photos");

        migrationBuilder.DropIndex(
            name: "ix_inspection_photos_tenant_object_key",
            schema: "inspections",
            table: "photos");

        migrationBuilder.DropIndex(
            name: "IX_photos_OrganizationId_SourcePhotoId",
            schema: "inspections",
            table: "photos");

        migrationBuilder.DropColumn(
            name: "SourcePhotoId",
            schema: "inspections",
            table: "photos");

        migrationBuilder.CreateIndex(
            name: "ux_inspection_photos_object_key",
            schema: "inspections",
            table: "photos",
            column: "ObjectKey",
            unique: true);
    }
}

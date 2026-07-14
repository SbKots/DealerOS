using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DealerOS.Api.Infrastructure.Migrations;

/// <inheritdoc />
public partial class VehiclePhotoGallery11 : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ux_vehicle_media_object_key",
            schema: "operations",
            table: "vehicle_media");

        migrationBuilder.DropCheckConstraint(
            name: "ck_vehicle_media_category",
            schema: "operations",
            table: "vehicle_media");

        migrationBuilder.AlterColumn<int>(
            name: "Category",
            schema: "operations",
            table: "vehicle_media",
            type: "integer",
            nullable: true,
            oldClrType: typeof(int),
            oldType: "integer");

        migrationBuilder.AddColumn<string>(
            name: "Caption",
            schema: "operations",
            table: "vehicle_media",
            type: "character varying(500)",
            maxLength: 500,
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "FocalPointX",
            schema: "operations",
            table: "vehicle_media",
            type: "numeric(5,4)",
            precision: 5,
            scale: 4,
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "FocalPointY",
            schema: "operations",
            table: "vehicle_media",
            type: "numeric(5,4)",
            precision: 5,
            scale: 4,
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "Height",
            schema: "operations",
            table: "vehicle_media",
            type: "integer",
            nullable: false,
            defaultValue: 1);

        migrationBuilder.AddColumn<bool>(
            name: "IsIncludedInListing",
            schema: "operations",
            table: "vehicle_media",
            type: "boolean",
            nullable: false,
            defaultValue: true);

        migrationBuilder.AddColumn<int>(
            name: "LargeHeight",
            schema: "operations",
            table: "vehicle_media",
            type: "integer",
            nullable: false,
            defaultValue: 1);

        migrationBuilder.AddColumn<string>(
            name: "LargeObjectKey",
            schema: "operations",
            table: "vehicle_media",
            type: "character varying(600)",
            maxLength: 600,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<int>(
            name: "LargeWidth",
            schema: "operations",
            table: "vehicle_media",
            type: "integer",
            nullable: false,
            defaultValue: 1);

        migrationBuilder.AddColumn<int>(
            name: "MediumHeight",
            schema: "operations",
            table: "vehicle_media",
            type: "integer",
            nullable: false,
            defaultValue: 1);

        migrationBuilder.AddColumn<string>(
            name: "MediumObjectKey",
            schema: "operations",
            table: "vehicle_media",
            type: "character varying(600)",
            maxLength: 600,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<int>(
            name: "MediumWidth",
            schema: "operations",
            table: "vehicle_media",
            type: "integer",
            nullable: false,
            defaultValue: 1);

        migrationBuilder.AddColumn<bool>(
            name: "OwnsOriginalObject",
            schema: "operations",
            table: "vehicle_media",
            type: "boolean",
            nullable: false,
            defaultValue: true);

        migrationBuilder.AddColumn<Guid>(
            name: "SourceInspectionPhotoId",
            schema: "operations",
            table: "vehicle_media",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "ThumbnailHeight",
            schema: "operations",
            table: "vehicle_media",
            type: "integer",
            nullable: false,
            defaultValue: 1);

        migrationBuilder.AddColumn<string>(
            name: "ThumbnailObjectKey",
            schema: "operations",
            table: "vehicle_media",
            type: "character varying(600)",
            maxLength: 600,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<int>(
            name: "ThumbnailWidth",
            schema: "operations",
            table: "vehicle_media",
            type: "integer",
            nullable: false,
            defaultValue: 1);

        migrationBuilder.AddColumn<int>(
            name: "Width",
            schema: "operations",
            table: "vehicle_media",
            type: "integer",
            nullable: false,
            defaultValue: 1);

        migrationBuilder.Sql("""
                UPDATE operations.vehicle_media
                SET "ThumbnailObjectKey" = "ObjectKey",
                    "MediumObjectKey" = "ObjectKey",
                    "LargeObjectKey" = "ObjectKey",
                    "IsIncludedInListing" = "Category" <> 4,
                    "OwnsOriginalObject" = TRUE;
                """);

        migrationBuilder.CreateIndex(
            name: "IX_vehicle_media_OrganizationId_SourceInspectionPhotoId",
            schema: "operations",
            table: "vehicle_media",
            columns: new[] { "OrganizationId", "SourceInspectionPhotoId" });

        migrationBuilder.CreateIndex(
            name: "ix_vehicle_media_original_object_key",
            schema: "operations",
            table: "vehicle_media",
            columns: new[] { "OrganizationId", "ObjectKey" });

        migrationBuilder.CreateIndex(
            name: "ux_vehicle_media_inspection_source",
            schema: "operations",
            table: "vehicle_media",
            columns: new[] { "OrganizationId", "VehicleId", "SourceInspectionPhotoId" },
            unique: true,
            filter: "\"SourceInspectionPhotoId\" IS NOT NULL");

        migrationBuilder.AddCheckConstraint(
            name: "ck_vehicle_media_category",
            schema: "operations",
            table: "vehicle_media",
            sql: "\"Category\" IS NULL OR \"Category\" BETWEEN 1 AND 12");

        migrationBuilder.AddCheckConstraint(
            name: "ck_vehicle_media_dimensions",
            schema: "operations",
            table: "vehicle_media",
            sql: "\"Width\" > 0 AND \"Height\" > 0 AND \"ThumbnailWidth\" > 0 AND \"ThumbnailHeight\" > 0 AND \"MediumWidth\" > 0 AND \"MediumHeight\" > 0 AND \"LargeWidth\" > 0 AND \"LargeHeight\" > 0");

        migrationBuilder.AddCheckConstraint(
            name: "ck_vehicle_media_focal_point",
            schema: "operations",
            table: "vehicle_media",
            sql: "(\"FocalPointX\" IS NULL AND \"FocalPointY\" IS NULL) OR (\"FocalPointX\" BETWEEN 0 AND 1 AND \"FocalPointY\" BETWEEN 0 AND 1)");

        migrationBuilder.AddForeignKey(
            name: "FK_vehicle_media_photos_OrganizationId_SourceInspectionPhotoId",
            schema: "operations",
            table: "vehicle_media",
            columns: new[] { "OrganizationId", "SourceInspectionPhotoId" },
            principalSchema: "inspections",
            principalTable: "photos",
            principalColumns: new[] { "OrganizationId", "Id" },
            onDelete: ReferentialAction.Restrict);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_vehicle_media_photos_OrganizationId_SourceInspectionPhotoId",
            schema: "operations",
            table: "vehicle_media");

        migrationBuilder.DropIndex(
            name: "IX_vehicle_media_OrganizationId_SourceInspectionPhotoId",
            schema: "operations",
            table: "vehicle_media");

        migrationBuilder.DropIndex(
            name: "ix_vehicle_media_original_object_key",
            schema: "operations",
            table: "vehicle_media");

        migrationBuilder.DropIndex(
            name: "ux_vehicle_media_inspection_source",
            schema: "operations",
            table: "vehicle_media");

        migrationBuilder.DropCheckConstraint(
            name: "ck_vehicle_media_category",
            schema: "operations",
            table: "vehicle_media");

        migrationBuilder.DropCheckConstraint(
            name: "ck_vehicle_media_dimensions",
            schema: "operations",
            table: "vehicle_media");

        migrationBuilder.DropCheckConstraint(
            name: "ck_vehicle_media_focal_point",
            schema: "operations",
            table: "vehicle_media");

        migrationBuilder.DropColumn(
            name: "Caption",
            schema: "operations",
            table: "vehicle_media");

        migrationBuilder.DropColumn(
            name: "FocalPointX",
            schema: "operations",
            table: "vehicle_media");

        migrationBuilder.DropColumn(
            name: "FocalPointY",
            schema: "operations",
            table: "vehicle_media");

        migrationBuilder.DropColumn(
            name: "Height",
            schema: "operations",
            table: "vehicle_media");

        migrationBuilder.DropColumn(
            name: "IsIncludedInListing",
            schema: "operations",
            table: "vehicle_media");

        migrationBuilder.DropColumn(
            name: "LargeHeight",
            schema: "operations",
            table: "vehicle_media");

        migrationBuilder.DropColumn(
            name: "LargeObjectKey",
            schema: "operations",
            table: "vehicle_media");

        migrationBuilder.DropColumn(
            name: "LargeWidth",
            schema: "operations",
            table: "vehicle_media");

        migrationBuilder.DropColumn(
            name: "MediumHeight",
            schema: "operations",
            table: "vehicle_media");

        migrationBuilder.DropColumn(
            name: "MediumObjectKey",
            schema: "operations",
            table: "vehicle_media");

        migrationBuilder.DropColumn(
            name: "MediumWidth",
            schema: "operations",
            table: "vehicle_media");

        migrationBuilder.DropColumn(
            name: "OwnsOriginalObject",
            schema: "operations",
            table: "vehicle_media");

        migrationBuilder.DropColumn(
            name: "SourceInspectionPhotoId",
            schema: "operations",
            table: "vehicle_media");

        migrationBuilder.DropColumn(
            name: "ThumbnailHeight",
            schema: "operations",
            table: "vehicle_media");

        migrationBuilder.DropColumn(
            name: "ThumbnailObjectKey",
            schema: "operations",
            table: "vehicle_media");

        migrationBuilder.DropColumn(
            name: "ThumbnailWidth",
            schema: "operations",
            table: "vehicle_media");

        migrationBuilder.DropColumn(
            name: "Width",
            schema: "operations",
            table: "vehicle_media");

        migrationBuilder.Sql("UPDATE operations.vehicle_media SET \"Category\" = 1 WHERE \"Category\" IS NULL OR \"Category\" > 4;");

        migrationBuilder.AlterColumn<int>(
            name: "Category",
            schema: "operations",
            table: "vehicle_media",
            type: "integer",
            nullable: false,
            defaultValue: 0,
            oldClrType: typeof(int),
            oldType: "integer",
            oldNullable: true);

        migrationBuilder.CreateIndex(
            name: "ux_vehicle_media_object_key",
            schema: "operations",
            table: "vehicle_media",
            column: "ObjectKey",
            unique: true);

        migrationBuilder.AddCheckConstraint(
            name: "ck_vehicle_media_category",
            schema: "operations",
            table: "vehicle_media",
            sql: "\"Category\" BETWEEN 1 AND 4");
    }
}

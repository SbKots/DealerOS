using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DealerOS.Api.Infrastructure.Migrations;

/// <inheritdoc />
public partial class GlobalUserEmailUniqueness : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ux_users_organization_email",
            schema: "identity",
            table: "users");

        migrationBuilder.CreateIndex(
            name: "ux_users_email",
            schema: "identity",
            table: "users",
            column: "Email",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ux_users_email",
            schema: "identity",
            table: "users");

        migrationBuilder.CreateIndex(
            name: "ux_users_organization_email",
            schema: "identity",
            table: "users",
            columns: new[] { "OrganizationId", "Email" },
            unique: true);
    }
}

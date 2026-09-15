using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShopHub.Modules.Catalog.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOfferSoftDelete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Offers_Code",
                schema: "catalog",
                table: "Offers");

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedUtc",
                schema: "catalog",
                table: "Offers",
                type: "datetime2(3)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                schema: "catalog",
                table: "Offers",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_Offers_Code",
                schema: "catalog",
                table: "Offers",
                column: "Code",
                unique: true,
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Offers_Code",
                schema: "catalog",
                table: "Offers");

            migrationBuilder.DropColumn(
                name: "DeletedUtc",
                schema: "catalog",
                table: "Offers");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                schema: "catalog",
                table: "Offers");

            migrationBuilder.CreateIndex(
                name: "IX_Offers_Code",
                schema: "catalog",
                table: "Offers",
                column: "Code",
                unique: true);
        }
    }
}

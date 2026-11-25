using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Filomat.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ItemExtraTags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExtraTags",
                table: "Items",
                type: "TEXT",
                nullable: false,
                defaultValue: "[]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExtraTags",
                table: "Items");
        }
    }
}

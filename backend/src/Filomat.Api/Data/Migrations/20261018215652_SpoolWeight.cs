using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Filomat.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class SpoolWeight : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "SpoolWeight",
                table: "Items",
                type: "REAL",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SpoolWeight",
                table: "Items");
        }
    }
}

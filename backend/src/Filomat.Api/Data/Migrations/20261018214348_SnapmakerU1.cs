using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Filomat.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class SnapmakerU1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Consumptions_PrinterId_JobId",
                table: "Consumptions");

            migrationBuilder.AddColumn<string>(
                name: "ToolStateJson",
                table: "Printers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TagUid",
                table: "Consumptions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Tool",
                table: "Consumptions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Consumptions_PrinterId_JobId_Tool",
                table: "Consumptions",
                columns: new[] { "PrinterId", "JobId", "Tool" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Consumptions_PrinterId_JobId_Tool",
                table: "Consumptions");

            migrationBuilder.DropColumn(
                name: "ToolStateJson",
                table: "Printers");

            migrationBuilder.DropColumn(
                name: "TagUid",
                table: "Consumptions");

            migrationBuilder.DropColumn(
                name: "Tool",
                table: "Consumptions");

            migrationBuilder.CreateIndex(
                name: "IX_Consumptions_PrinterId_JobId",
                table: "Consumptions",
                columns: new[] { "PrinterId", "JobId" },
                unique: true);
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Filomat.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class SpoolContainerAndTransmission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContainerId",
                table: "Spools",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "TransmissionDistance",
                table: "Items",
                type: "REAL",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Spools_ContainerId",
                table: "Spools",
                column: "ContainerId");

            migrationBuilder.AddForeignKey(
                name: "FK_Spools_Containers_ContainerId",
                table: "Spools",
                column: "ContainerId",
                principalTable: "Containers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Spools_Containers_ContainerId",
                table: "Spools");

            migrationBuilder.DropIndex(
                name: "IX_Spools_ContainerId",
                table: "Spools");

            migrationBuilder.DropColumn(
                name: "ContainerId",
                table: "Spools");

            migrationBuilder.DropColumn(
                name: "TransmissionDistance",
                table: "Items");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Filomat.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Spools : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SpoolsInStock",
                table: "Items",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SpoolId",
                table: "Consumptions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Spools",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    ItemId = table.Column<string>(type: "TEXT", nullable: false),
                    Number = table.Column<int>(type: "INTEGER", nullable: false),
                    RemainingWeight = table.Column<double>(type: "REAL", nullable: true),
                    PrinterId = table.Column<string>(type: "TEXT", nullable: true),
                    Tool = table.Column<int>(type: "INTEGER", nullable: true),
                    OpenedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EmptiedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Spools", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Spools_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Spools_Printers_PrinterId",
                        column: x => x.PrinterId,
                        principalTable: "Printers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            // Bisheriges Restgewicht pro Filament wird zur ersten angebrochenen Spule
            migrationBuilder.Sql("""
                INSERT INTO Spools (Id, ItemId, Number, RemainingWeight, PrinterId, Tool, OpenedAt, EmptiedAt)
                SELECT lower(hex(randomblob(16))), Id, 1, RemainingWeight, NULL, NULL, strftime('%Y-%m-%d %H:%M:%f', 'now'), NULL
                FROM Items WHERE RemainingWeight IS NOT NULL
                """);

            migrationBuilder.DropColumn(
                name: "RemainingWeight",
                table: "Items");

            migrationBuilder.CreateIndex(
                name: "IX_Consumptions_SpoolId",
                table: "Consumptions",
                column: "SpoolId");

            migrationBuilder.CreateIndex(
                name: "IX_Spools_ItemId_Number",
                table: "Spools",
                columns: new[] { "ItemId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Spools_PrinterId_Tool",
                table: "Spools",
                columns: new[] { "PrinterId", "Tool" });

            migrationBuilder.AddForeignKey(
                name: "FK_Consumptions_Spools_SpoolId",
                table: "Consumptions",
                column: "SpoolId",
                principalTable: "Spools",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Consumptions_Spools_SpoolId",
                table: "Consumptions");

            migrationBuilder.DropTable(
                name: "Spools");

            migrationBuilder.DropIndex(
                name: "IX_Consumptions_SpoolId",
                table: "Consumptions");

            migrationBuilder.DropColumn(
                name: "SpoolsInStock",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "SpoolId",
                table: "Consumptions");

            migrationBuilder.AddColumn<double>(
                name: "RemainingWeight",
                table: "Items",
                type: "REAL",
                nullable: true);
        }
    }
}

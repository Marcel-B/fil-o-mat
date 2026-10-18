using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Filomat.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Printers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "NetWeight",
                table: "Items",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "RemainingWeight",
                table: "Items",
                type: "REAL",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Printers",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", nullable: false),
                    BaseUrl = table.Column<string>(type: "TEXT", nullable: false),
                    ApiKey = table.Column<string>(type: "TEXT", nullable: true),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    ContainerId = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastJobEndedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastSyncAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastSyncError = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Printers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Printers_Containers_ContainerId",
                        column: x => x.ContainerId,
                        principalTable: "Containers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Consumptions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    PrinterId = table.Column<string>(type: "TEXT", nullable: false),
                    JobId = table.Column<string>(type: "TEXT", nullable: false),
                    JobName = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    EndedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LengthMm = table.Column<double>(type: "REAL", nullable: false),
                    Grams = table.Column<double>(type: "REAL", nullable: false),
                    ItemId = table.Column<string>(type: "TEXT", nullable: true),
                    RecordedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Consumptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Consumptions_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Consumptions_Printers_PrinterId",
                        column: x => x.PrinterId,
                        principalTable: "Printers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Consumptions_ItemId",
                table: "Consumptions",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_Consumptions_PrinterId_JobId",
                table: "Consumptions",
                columns: new[] { "PrinterId", "JobId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Printers_ContainerId",
                table: "Printers",
                column: "ContainerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Consumptions");

            migrationBuilder.DropTable(
                name: "Printers");

            migrationBuilder.DropColumn(
                name: "NetWeight",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "RemainingWeight",
                table: "Items");
        }
    }
}

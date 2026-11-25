using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Filomat.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class PrinterReader : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ReaderSeenAt",
                table: "Printers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReaderSlotsJson",
                table: "Printers",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReaderSeenAt",
                table: "Printers");

            migrationBuilder.DropColumn(
                name: "ReaderSlotsJson",
                table: "Printers");
        }
    }
}

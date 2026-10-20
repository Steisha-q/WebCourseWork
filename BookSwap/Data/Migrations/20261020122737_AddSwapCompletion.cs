using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookSwap.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSwapCompletion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CompletedAt",
                table: "SwapRequests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ReceiverConfirmed",
                table: "SwapRequests",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "SenderConfirmed",
                table: "SwapRequests",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CompletedAt",
                table: "SwapRequests");

            migrationBuilder.DropColumn(
                name: "ReceiverConfirmed",
                table: "SwapRequests");

            migrationBuilder.DropColumn(
                name: "SenderConfirmed",
                table: "SwapRequests");
        }
    }
}

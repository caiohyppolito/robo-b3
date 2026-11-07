using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RoboB3.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderIntentsAndDecisionContext : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ClientOrderId",
                schema: "robo",
                table: "TradingDecisions",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CodeVersion",
                schema: "robo",
                table: "TradingDecisions",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Features",
                schema: "robo",
                table: "TradingDecisions",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "ModelId",
                schema: "robo",
                table: "TradingDecisions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Parameters",
                schema: "robo",
                table: "TradingDecisions",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "MaePercent",
                schema: "robo",
                table: "TradeJournal",
                type: "decimal(10,4)",
                precision: 10,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MfePercent",
                schema: "robo",
                table: "TradeJournal",
                type: "decimal(10,4)",
                precision: 10,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MaxAdverse",
                schema: "robo",
                table: "PaperPositions",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "MaxFavorable",
                schema: "robo",
                table: "PaperPositions",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "OrderIntents",
                schema: "robo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Mode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    ClientOrderId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Purpose = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Symbol = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Side = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    ReferencePrice = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    StopLoss = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    AssignmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PositionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    StrategyKey = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    FillPrice = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    FillQuantity = table.Column<int>(type: "int", nullable: true),
                    BrokerTicket = table.Column<long>(type: "bigint", nullable: true),
                    LatencyMs = table.Column<int>(type: "int", nullable: true),
                    Message = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderIntents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrderIntents_ClientOrderId",
                schema: "robo",
                table: "OrderIntents",
                column: "ClientOrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderIntents_Mode_Status",
                schema: "robo",
                table: "OrderIntents",
                columns: new[] { "Mode", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrderIntents",
                schema: "robo");

            migrationBuilder.DropColumn(
                name: "ClientOrderId",
                schema: "robo",
                table: "TradingDecisions");

            migrationBuilder.DropColumn(
                name: "CodeVersion",
                schema: "robo",
                table: "TradingDecisions");

            migrationBuilder.DropColumn(
                name: "Features",
                schema: "robo",
                table: "TradingDecisions");

            migrationBuilder.DropColumn(
                name: "ModelId",
                schema: "robo",
                table: "TradingDecisions");

            migrationBuilder.DropColumn(
                name: "Parameters",
                schema: "robo",
                table: "TradingDecisions");

            migrationBuilder.DropColumn(
                name: "MaePercent",
                schema: "robo",
                table: "TradeJournal");

            migrationBuilder.DropColumn(
                name: "MfePercent",
                schema: "robo",
                table: "TradeJournal");

            migrationBuilder.DropColumn(
                name: "MaxAdverse",
                schema: "robo",
                table: "PaperPositions");

            migrationBuilder.DropColumn(
                name: "MaxFavorable",
                schema: "robo",
                table: "PaperPositions");
        }
    }
}

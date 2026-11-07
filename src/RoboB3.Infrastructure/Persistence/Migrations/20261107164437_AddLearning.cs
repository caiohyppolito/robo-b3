using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RoboB3.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLearning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "MetaModelId",
                schema: "robo",
                table: "BacktestRuns",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Metrics_SignalsFiltered",
                schema: "robo",
                table: "BacktestRuns",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "MetaModels",
                schema: "robo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StrategyKey = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Decision = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    TrainingFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    SplitDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ValidationTo = table.Column<DateOnly>(type: "date", nullable: false),
                    TrainingExamples = table.Column<int>(type: "int", nullable: false),
                    ValidationExamples = table.Column<int>(type: "int", nullable: false),
                    Threshold = table.Column<double>(type: "float", nullable: false),
                    Auc = table.Column<double>(type: "float", nullable: false),
                    BaseWinRatePercent = table.Column<decimal>(type: "decimal(10,4)", precision: 10, scale: 4, nullable: false),
                    FilteredWinRatePercent = table.Column<decimal>(type: "decimal(10,4)", precision: 10, scale: 4, nullable: false),
                    BaseAverageReturnPercent = table.Column<decimal>(type: "decimal(10,4)", precision: 10, scale: 4, nullable: false),
                    FilteredAverageReturnPercent = table.Column<decimal>(type: "decimal(10,4)", precision: 10, scale: 4, nullable: false),
                    KeptPercent = table.Column<decimal>(type: "decimal(10,4)", precision: 10, scale: 4, nullable: false),
                    UnfilteredSharpe = table.Column<decimal>(type: "decimal(10,4)", precision: 10, scale: 4, nullable: false),
                    FilteredSharpe = table.Column<decimal>(type: "decimal(10,4)", precision: 10, scale: 4, nullable: false),
                    ChampionSharpe = table.Column<decimal>(type: "decimal(10,4)", precision: 10, scale: 4, nullable: true),
                    FeatureImportance = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ModelBytes = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    ActivatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RetiredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Analysis = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MetaModels", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TradeJournal",
                schema: "robo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    StrategyKey = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Symbol = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Direction = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    EntryDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ExitDate = table.Column<DateOnly>(type: "date", nullable: false),
                    NetProfit = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ReturnPercent = table.Column<decimal>(type: "decimal(10,4)", precision: 10, scale: 4, nullable: false),
                    Features = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ModelProbability = table.Column<double>(type: "float", nullable: true),
                    RecordedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TradeJournal", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MetaModels_CreatedAt",
                schema: "robo",
                table: "MetaModels",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_MetaModels_StrategyKey",
                schema: "robo",
                table: "MetaModels",
                column: "StrategyKey",
                unique: true,
                filter: "[Status] = 'Active'");

            migrationBuilder.CreateIndex(
                name: "IX_TradeJournal_StrategyKey_EntryDate",
                schema: "robo",
                table: "TradeJournal",
                columns: new[] { "StrategyKey", "EntryDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MetaModels",
                schema: "robo");

            migrationBuilder.DropTable(
                name: "TradeJournal",
                schema: "robo");

            migrationBuilder.DropColumn(
                name: "MetaModelId",
                schema: "robo",
                table: "BacktestRuns");

            migrationBuilder.DropColumn(
                name: "Metrics_SignalsFiltered",
                schema: "robo",
                table: "BacktestRuns");
        }
    }
}

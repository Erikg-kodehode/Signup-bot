using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MorningSignInBot.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreateWithStageConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SignIns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    Username = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SignInType = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SignIns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StageNotificationConfigs",
                columns: table => new
                {
                    StageChannelId = table.Column<ulong>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    NotificationRoleId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    NotificationChannelId = table.Column<ulong>(type: "INTEGER", nullable: true),
                    CustomMessage = table.Column<string>(type: "TEXT", nullable: true),
                    GuildId = table.Column<ulong>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StageNotificationConfigs", x => x.StageChannelId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SignIns_Timestamp",
                table: "SignIns",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_SignIns_UserId",
                table: "SignIns",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_StageNotificationConfigs_GuildId",
                table: "StageNotificationConfigs",
                column: "GuildId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SignIns");

            migrationBuilder.DropTable(
                name: "StageNotificationConfigs");
        }
    }
}

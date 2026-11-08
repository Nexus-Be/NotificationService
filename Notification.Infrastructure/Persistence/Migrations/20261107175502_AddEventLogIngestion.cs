using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Notification.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEventLogIngestion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Headers",
                table: "EventLogs",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestHash",
                table: "EventLogs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TraceId",
                table: "EventLogs",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_EventLogs_ActorId_OccurredAt",
                table: "EventLogs",
                columns: new[] { "ActorId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_EventLogs_EventType_OccurredAt",
                table: "EventLogs",
                columns: new[] { "EventType", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_EventLogs_OccurredAt_Id",
                table: "EventLogs",
                columns: new[] { "OccurredAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_EventLogs_SourceService_EventId",
                table: "EventLogs",
                columns: new[] { "SourceService", "EventId" },
                unique: true,
                filter: "\"EventId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_EventLogs_SourceService_OccurredAt",
                table: "EventLogs",
                columns: new[] { "SourceService", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_EventLogs_TraceId",
                table: "EventLogs",
                column: "TraceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_EventLogs_ActorId_OccurredAt",
                table: "EventLogs");

            migrationBuilder.DropIndex(
                name: "IX_EventLogs_EventType_OccurredAt",
                table: "EventLogs");

            migrationBuilder.DropIndex(
                name: "IX_EventLogs_OccurredAt_Id",
                table: "EventLogs");

            migrationBuilder.DropIndex(
                name: "IX_EventLogs_SourceService_EventId",
                table: "EventLogs");

            migrationBuilder.DropIndex(
                name: "IX_EventLogs_SourceService_OccurredAt",
                table: "EventLogs");

            migrationBuilder.DropIndex(
                name: "IX_EventLogs_TraceId",
                table: "EventLogs");

            migrationBuilder.DropColumn(
                name: "Headers",
                table: "EventLogs");

            migrationBuilder.DropColumn(
                name: "RequestHash",
                table: "EventLogs");

            migrationBuilder.DropColumn(
                name: "TraceId",
                table: "EventLogs");
        }
    }
}

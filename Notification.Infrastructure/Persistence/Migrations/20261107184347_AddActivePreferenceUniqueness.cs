using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Notification.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddActivePreferenceUniqueness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_NotificationPreferences_UserId_NotificationType_ChannelCode",
                table: "NotificationPreferences",
                columns: new[] { "UserId", "NotificationType", "ChannelCode" },
                unique: true,
                filter: "NOT \"IsDeleted\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_NotificationPreferences_UserId_NotificationType_ChannelCode",
                table: "NotificationPreferences");
        }
    }
}

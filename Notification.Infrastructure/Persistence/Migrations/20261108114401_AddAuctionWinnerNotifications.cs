using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Notification.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAuctionWinnerNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BodyOverride",
                table: "NotificationDeliveries",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubjectOverride",
                table: "NotificationDeliveries",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AuctionWinnerDispatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AuctionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResultId = table.Column<Guid>(type: "uuid", nullable: false),
                    WinningBidId = table.Column<Guid>(type: "uuid", nullable: false),
                    NotificationId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuctionWinnerDispatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuctionWinnerDispatches_Notifications_NotificationId",
                        column: x => x.NotificationId,
                        principalTable: "Notifications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.UpdateData(
                table: "NotificationDeliveries",
                keyColumn: "Id",
                keyValue: new Guid("64000000-0000-0000-0000-000000000001"),
                columns: new[] { "BodyOverride", "SubjectOverride" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "NotificationDeliveries",
                keyColumn: "Id",
                keyValue: new Guid("64000000-0000-0000-0000-000000000002"),
                columns: new[] { "BodyOverride", "SubjectOverride" },
                values: new object[] { null, null });

            migrationBuilder.InsertData(
                table: "NotificationTemplates",
                columns: new[] { "Id", "BodyTemplate", "ChannelCode", "Code", "CreatedAt", "CreatedBy", "DeletedAt", "IsActive", "IsDeleted", "IsMandatory", "Locale", "NotificationType", "SubjectTemplate", "UpdatedAt", "UpdatedBy" },
                values: new object[,]
                {
                    { new Guid("60000000-0000-0000-0000-00000000000f"), "<p>Xin chào {{FullName}},</p><p>Bạn đã thắng phiên {{AuctionTitle}}, kết quả chốt lúc {{SettledAtUtc}}.</p><p>Giá thắng: {{WinningAmount}} VND. Hạn thanh toán: {{PaymentDeadlineUtc}}.</p><p>Tham chiếu đơn: {{OrderReference}}</p><p>Hãy mở chi tiết phiên và đơn đấu giá liên quan trong ứng dụng Nexus. Kiểm tra số tiền cuối cùng và chỉ thanh toán khi hệ thống xác nhận đơn còn hiệu lực. Thông báo này không xác nhận đã thanh toán.</p>", 0, "AUCTION_WINNER_EMAIL", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("00000000-0000-0000-0000-000000000000"), null, true, false, true, "vi-VN", 3, "Kết quả thắng đấu giá và hướng dẫn thanh toán", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("00000000-0000-0000-0000-000000000000") },
                    { new Guid("60000000-0000-0000-0000-000000000010"), "Bạn đã thắng phiên {{AuctionTitle}} với giá {{WinningAmount}} VND, chốt lúc {{SettledAtUtc}}. Hạn thanh toán: {{PaymentDeadlineUtc}}. Kiểm tra đơn còn hiệu lực trong ứng dụng Nexus trước khi thanh toán.", 1, "AUCTION_WINNER_PUSH", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("00000000-0000-0000-0000-000000000000"), null, true, false, false, "vi-VN", 3, "Bạn đã thắng đấu giá", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("00000000-0000-0000-0000-000000000000") }
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuctionWinnerDispatches_AuctionId",
                table: "AuctionWinnerDispatches",
                column: "AuctionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuctionWinnerDispatches_NotificationId",
                table: "AuctionWinnerDispatches",
                column: "NotificationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuctionWinnerDispatches_ResultId",
                table: "AuctionWinnerDispatches",
                column: "ResultId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuctionWinnerDispatches_WinningBidId",
                table: "AuctionWinnerDispatches",
                column: "WinningBidId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuctionWinnerDispatches");

            migrationBuilder.DeleteData(
                table: "NotificationTemplates",
                keyColumn: "Id",
                keyValue: new Guid("60000000-0000-0000-0000-00000000000f"));

            migrationBuilder.DeleteData(
                table: "NotificationTemplates",
                keyColumn: "Id",
                keyValue: new Guid("60000000-0000-0000-0000-000000000010"));

            migrationBuilder.DropColumn(
                name: "BodyOverride",
                table: "NotificationDeliveries");

            migrationBuilder.DropColumn(
                name: "SubjectOverride",
                table: "NotificationDeliveries");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Notification.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAuctionPaymentTimeoutNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuctionPaymentTimeoutDispatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AuctionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResultId = table.Column<Guid>(type: "uuid", nullable: false),
                    WinningBidId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    NotificationId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuctionPaymentTimeoutDispatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuctionPaymentTimeoutDispatches_Notifications_NotificationId",
                        column: x => x.NotificationId,
                        principalTable: "Notifications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "NotificationTemplates",
                columns: new[] { "Id", "BodyTemplate", "ChannelCode", "Code", "CreatedAt", "CreatedBy", "DeletedAt", "IsActive", "IsDeleted", "IsMandatory", "Locale", "NotificationType", "SubjectTemplate", "UpdatedAt", "UpdatedBy" },
                values: new object[,]
                {
                    { new Guid("60000000-0000-0000-0000-000000000012"), "<p>Xin chào {{FullName}},</p><p>Bạn đã quá hạn thanh toán đơn {{OrderReference}} của phiên {{AuctionTitle}}. Hạn thanh toán: {{PaymentDeadlineUtc}}.</p><p>Kết quả ghi nhận lúc {{TimedOutAtUtc}}: đơn đã bị hủy, phiên kết thúc thất bại.</p><p>Hãy kiểm tra trạng thái đơn và thông tin uy tín hiện tại trong ứng dụng Nexus. Thông báo này không yêu cầu thanh toán khoản phạt tiền.</p>", 0, "AUCTION_PAYMENT_TIMEOUT_EMAIL", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("00000000-0000-0000-0000-000000000000"), null, true, false, true, "vi-VN", 9, "Đơn đấu giá đã quá hạn thanh toán", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("00000000-0000-0000-0000-000000000000") },
                    { new Guid("60000000-0000-0000-0000-000000000013"), "Bạn đã quá hạn thanh toán phiên {{AuctionTitle}} (hạn {{PaymentDeadlineUtc}}). Kết quả hủy đơn ghi nhận lúc {{TimedOutAtUtc}}. Vui lòng kiểm tra trạng thái hiện tại trong ứng dụng Nexus.", 1, "AUCTION_PAYMENT_TIMEOUT_PUSH", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("00000000-0000-0000-0000-000000000000"), null, true, false, false, "vi-VN", 9, "Đơn đấu giá đã quá hạn thanh toán", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("00000000-0000-0000-0000-000000000000") }
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuctionPaymentTimeoutDispatches_AuctionId",
                table: "AuctionPaymentTimeoutDispatches",
                column: "AuctionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuctionPaymentTimeoutDispatches_NotificationId",
                table: "AuctionPaymentTimeoutDispatches",
                column: "NotificationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuctionPaymentTimeoutDispatches_OrderId",
                table: "AuctionPaymentTimeoutDispatches",
                column: "OrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuctionPaymentTimeoutDispatches_ResultId",
                table: "AuctionPaymentTimeoutDispatches",
                column: "ResultId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuctionPaymentTimeoutDispatches_WinningBidId",
                table: "AuctionPaymentTimeoutDispatches",
                column: "WinningBidId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuctionPaymentTimeoutDispatches");

            migrationBuilder.DeleteData(
                table: "NotificationTemplates",
                keyColumn: "Id",
                keyValue: new Guid("60000000-0000-0000-0000-000000000012"));

            migrationBuilder.DeleteData(
                table: "NotificationTemplates",
                keyColumn: "Id",
                keyValue: new Guid("60000000-0000-0000-0000-000000000013"));
        }
    }
}

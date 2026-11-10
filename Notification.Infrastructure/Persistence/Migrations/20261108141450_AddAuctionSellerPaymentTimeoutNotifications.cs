using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Notification.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAuctionSellerPaymentTimeoutNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuctionSellerPaymentTimeoutDispatches",
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
                    table.PrimaryKey("PK_AuctionSellerPaymentTimeoutDispatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuctionSellerPaymentTimeoutDispatches_Notifications_Notific~",
                        column: x => x.NotificationId,
                        principalTable: "Notifications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "NotificationTemplates",
                columns: new[] { "Id", "BodyTemplate", "ChannelCode", "Code", "CreatedAt", "CreatedBy", "DeletedAt", "IsActive", "IsDeleted", "IsMandatory", "Locale", "NotificationType", "SubjectTemplate", "UpdatedAt", "UpdatedBy" },
                values: new object[] { new Guid("60000000-0000-0000-0000-000000000016"), "Người thắng phiên {{AuctionTitle}} đã quá hạn thanh toán (hạn gốc {{PaymentDeadlineUtc}}). Kết quả ghi nhận lúc {{TimedOutAtUtc}}: đơn đã bị hủy, phiên thất bại. Vui lòng kiểm tra phiên và đơn hiện tại trong Nexus.", 1, "AUCTION_SELLER_PAYMENT_TIMEOUT_PUSH", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("00000000-0000-0000-0000-000000000000"), null, true, false, false, "vi-VN", 9, "Người thắng đã quá hạn thanh toán", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("00000000-0000-0000-0000-000000000000") });

            migrationBuilder.CreateIndex(
                name: "IX_AuctionSellerPaymentTimeoutDispatches_AuctionId",
                table: "AuctionSellerPaymentTimeoutDispatches",
                column: "AuctionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuctionSellerPaymentTimeoutDispatches_NotificationId",
                table: "AuctionSellerPaymentTimeoutDispatches",
                column: "NotificationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuctionSellerPaymentTimeoutDispatches_OrderId",
                table: "AuctionSellerPaymentTimeoutDispatches",
                column: "OrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuctionSellerPaymentTimeoutDispatches_ResultId",
                table: "AuctionSellerPaymentTimeoutDispatches",
                column: "ResultId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuctionSellerPaymentTimeoutDispatches_WinningBidId",
                table: "AuctionSellerPaymentTimeoutDispatches",
                column: "WinningBidId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuctionSellerPaymentTimeoutDispatches");

            migrationBuilder.DeleteData(
                table: "NotificationTemplates",
                keyColumn: "Id",
                keyValue: new Guid("60000000-0000-0000-0000-000000000016"));
        }
    }
}

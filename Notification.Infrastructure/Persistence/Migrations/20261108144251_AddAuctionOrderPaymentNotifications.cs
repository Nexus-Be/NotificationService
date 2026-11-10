using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Notification.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAuctionOrderPaymentNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuctionOrderPaidDispatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AuctionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResultId = table.Column<Guid>(type: "uuid", nullable: false),
                    WinningBidId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    BuyerNotificationId = table.Column<Guid>(type: "uuid", nullable: false),
                    SellerNotificationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuctionOrderPaidDispatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuctionOrderPaidDispatches_Notifications_BuyerNotificationId",
                        column: x => x.BuyerNotificationId,
                        principalTable: "Notifications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AuctionOrderPaidDispatches_Notifications_SellerNotification~",
                        column: x => x.SellerNotificationId,
                        principalTable: "Notifications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "NotificationTemplates",
                columns: new[] { "Id", "BodyTemplate", "ChannelCode", "Code", "CreatedAt", "CreatedBy", "DeletedAt", "IsActive", "IsDeleted", "IsMandatory", "Locale", "NotificationType", "SubjectTemplate", "UpdatedAt", "UpdatedBy" },
                values: new object[,]
                {
                    { new Guid("60000000-0000-0000-0000-000000000018"), "Đơn đấu giá {{OrderNo}} đã thanh toán {{TotalAmount}} VND,ghi nhận lúc {{PaidAtUtc}}. Vui lòng kiểm tra trạng thái đơn hiện tại trong Nexus.", 1, "AUCTION_ORDER_PAID_BUYER_PUSH", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("00000000-0000-0000-0000-000000000000"), null, true, false, true, "vi-VN", 0, "Đơn đấu giá đã thanh toán thành công", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("00000000-0000-0000-0000-000000000000") },
                    { new Guid("60000000-0000-0000-0000-000000000019"), "Đơn đấu giá {{OrderNo}} đã được thanh toán,ghi nhận lúc {{PaidAtUtc}}. Hạn chấp nhận từ nguồn: {{AcceptDeadlineUtc}}. Vui lòng mở đơn trong Nexus,kiểm tra trạng thái và quyền chấp nhận hiện tại trước khi thao tác.", 1, "AUCTION_ORDER_PAID_SELLER_PUSH", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("00000000-0000-0000-0000-000000000000"), null, true, false, true, "vi-VN", 0, "Đơn đấu giá đã được thanh toán", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("00000000-0000-0000-0000-000000000000") }
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuctionOrderPaidDispatches_AuctionId",
                table: "AuctionOrderPaidDispatches",
                column: "AuctionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuctionOrderPaidDispatches_BuyerNotificationId",
                table: "AuctionOrderPaidDispatches",
                column: "BuyerNotificationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuctionOrderPaidDispatches_OrderId",
                table: "AuctionOrderPaidDispatches",
                column: "OrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuctionOrderPaidDispatches_ResultId",
                table: "AuctionOrderPaidDispatches",
                column: "ResultId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuctionOrderPaidDispatches_SellerNotificationId",
                table: "AuctionOrderPaidDispatches",
                column: "SellerNotificationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuctionOrderPaidDispatches_WinningBidId",
                table: "AuctionOrderPaidDispatches",
                column: "WinningBidId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuctionOrderPaidDispatches");

            migrationBuilder.DeleteData(
                table: "NotificationTemplates",
                keyColumn: "Id",
                keyValue: new Guid("60000000-0000-0000-0000-000000000018"));

            migrationBuilder.DeleteData(
                table: "NotificationTemplates",
                keyColumn: "Id",
                keyValue: new Guid("60000000-0000-0000-0000-000000000019"));
        }
    }
}

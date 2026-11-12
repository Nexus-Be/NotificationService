using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Notification.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderCompletionNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OrderCompletionDispatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderKind = table.Column<int>(type: "integer", nullable: false),
                    AuctionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ShipmentId = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("PK_OrderCompletionDispatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderCompletionDispatches_Notifications_BuyerNotificationId",
                        column: x => x.BuyerNotificationId,
                        principalTable: "Notifications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrderCompletionDispatches_Notifications_SellerNotificationId",
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
                    { new Guid("60000000-0000-0000-0000-000000000033"), "Đơn {{OrderNo}} đã hoàn tất,ghi nhận lúc {{CompletedAtUtc}}. Vui lòng mở đơn trong Nexus để xem chi tiết và các lựa chọn đánh giá hiện có.", 1, "ORDER_COMPLETED_BUYER_PUSH", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("00000000-0000-0000-0000-000000000000"), null, true, false, true, "vi-VN", 14, "Đơn hàng đã hoàn tất", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("00000000-0000-0000-0000-000000000000") },
                    { new Guid("60000000-0000-0000-0000-000000000034"), "Đơn {{OrderNo}} đã hoàn tất,ghi nhận lúc {{CompletedAtUtc}}. Vui lòng kiểm tra chi tiết đơn và trạng thái thanh toán cho người bán trong Nexus.", 1, "ORDER_COMPLETED_SELLER_PUSH", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("00000000-0000-0000-0000-000000000000"), null, true, false, true, "vi-VN", 14, "Đơn hàng đã hoàn tất", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("00000000-0000-0000-0000-000000000000") }
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrderCompletionDispatches_AuctionId",
                table: "OrderCompletionDispatches",
                column: "AuctionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderCompletionDispatches_BuyerNotificationId",
                table: "OrderCompletionDispatches",
                column: "BuyerNotificationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderCompletionDispatches_OrderKind_OrderId",
                table: "OrderCompletionDispatches",
                columns: new[] { "OrderKind", "OrderId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderCompletionDispatches_SellerNotificationId",
                table: "OrderCompletionDispatches",
                column: "SellerNotificationId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrderCompletionDispatches");

            migrationBuilder.DeleteData(
                table: "NotificationTemplates",
                keyColumn: "Id",
                keyValue: new Guid("60000000-0000-0000-0000-000000000033"));

            migrationBuilder.DeleteData(
                table: "NotificationTemplates",
                keyColumn: "Id",
                keyValue: new Guid("60000000-0000-0000-0000-000000000034"));
        }
    }
}

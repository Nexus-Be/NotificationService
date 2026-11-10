using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Notification.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderDecisionNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OrderDecisionDispatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderKind = table.Column<int>(type: "integer", nullable: false),
                    AuctionId = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("PK_OrderDecisionDispatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderDecisionDispatches_Notifications_BuyerNotificationId",
                        column: x => x.BuyerNotificationId,
                        principalTable: "Notifications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrderDecisionDispatches_Notifications_SellerNotificationId",
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
                    { new Guid("60000000-0000-0000-0000-00000000001a"), "Seller đã chấp nhận xử lý đơn {{OrderNo}},ghi nhận lúc {{DecidedAtUtc}}. Vui lòng kiểm tra trạng thái đơn hiện tại trong Nexus.", 1, "ORDER_ACCEPTED_BUYER_PUSH", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("00000000-0000-0000-0000-000000000000"), null, true, false, true, "vi-VN", 12, "Seller đã chấp nhận đơn", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("00000000-0000-0000-0000-000000000000") },
                    { new Guid("60000000-0000-0000-0000-00000000001b"), "Seller đã chấp nhận xử lý đơn {{OrderNo}},ghi nhận lúc {{DecidedAtUtc}}. Vui lòng kiểm tra trạng thái đơn hiện tại trong Nexus.", 1, "ORDER_ACCEPTED_SELLER_PUSH", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("00000000-0000-0000-0000-000000000000"), null, true, false, true, "vi-VN", 12, "Seller đã chấp nhận đơn", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("00000000-0000-0000-0000-000000000000") },
                    { new Guid("60000000-0000-0000-0000-00000000001c"), "Đơn {{OrderNo}} bị hủy do Seller từ chối,ghi nhận lúc {{DecidedAtUtc}}. Lý do: {{Reason}}. Vui lòng kiểm tra trạng thái đơn và hoàn tiền trong Nexus.", 1, "ORDER_REJECTED_BUYER_PUSH", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("00000000-0000-0000-0000-000000000000"), null, true, false, true, "vi-VN", 12, "Đơn hàng đã bị hủy do Seller từ chối", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("00000000-0000-0000-0000-000000000000") },
                    { new Guid("60000000-0000-0000-0000-00000000001d"), "Đơn {{OrderNo}} bị hủy do Seller từ chối,ghi nhận lúc {{DecidedAtUtc}}. Lý do: {{Reason}}. Vui lòng kiểm tra trạng thái đơn và hoàn tiền trong Nexus.", 1, "ORDER_REJECTED_SELLER_PUSH", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("00000000-0000-0000-0000-000000000000"), null, true, false, true, "vi-VN", 12, "Đơn hàng đã bị hủy do Seller từ chối", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("00000000-0000-0000-0000-000000000000") },
                    { new Guid("60000000-0000-0000-0000-00000000001e"), "Đơn {{OrderNo}} bị hủy do quá hạn Seller chấp nhận,ghi nhận lúc {{DecidedAtUtc}}. Lý do: {{Reason}}. Vui lòng kiểm tra trạng thái đơn và hoàn tiền trong Nexus.", 1, "ORDER_ACCEPTANCE_TIMEOUT_BUYER_PUSH", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("00000000-0000-0000-0000-000000000000"), null, true, false, true, "vi-VN", 12, "Đơn hàng đã bị hủy do quá hạn chấp nhận", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("00000000-0000-0000-0000-000000000000") },
                    { new Guid("60000000-0000-0000-0000-00000000001f"), "Đơn {{OrderNo}} bị hủy do quá hạn Seller chấp nhận,ghi nhận lúc {{DecidedAtUtc}}. Lý do: {{Reason}}. Vui lòng kiểm tra trạng thái đơn và hoàn tiền trong Nexus.", 1, "ORDER_ACCEPTANCE_TIMEOUT_SELLER_PUSH", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("00000000-0000-0000-0000-000000000000"), null, true, false, true, "vi-VN", 12, "Đơn hàng đã bị hủy do quá hạn chấp nhận", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("00000000-0000-0000-0000-000000000000") }
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrderDecisionDispatches_AuctionId",
                table: "OrderDecisionDispatches",
                column: "AuctionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderDecisionDispatches_BuyerNotificationId",
                table: "OrderDecisionDispatches",
                column: "BuyerNotificationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderDecisionDispatches_OrderKind_OrderId",
                table: "OrderDecisionDispatches",
                columns: new[] { "OrderKind", "OrderId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderDecisionDispatches_SellerNotificationId",
                table: "OrderDecisionDispatches",
                column: "SellerNotificationId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrderDecisionDispatches");

            migrationBuilder.DeleteData(
                table: "NotificationTemplates",
                keyColumn: "Id",
                keyValue: new Guid("60000000-0000-0000-0000-00000000001a"));

            migrationBuilder.DeleteData(
                table: "NotificationTemplates",
                keyColumn: "Id",
                keyValue: new Guid("60000000-0000-0000-0000-00000000001b"));

            migrationBuilder.DeleteData(
                table: "NotificationTemplates",
                keyColumn: "Id",
                keyValue: new Guid("60000000-0000-0000-0000-00000000001c"));

            migrationBuilder.DeleteData(
                table: "NotificationTemplates",
                keyColumn: "Id",
                keyValue: new Guid("60000000-0000-0000-0000-00000000001d"));

            migrationBuilder.DeleteData(
                table: "NotificationTemplates",
                keyColumn: "Id",
                keyValue: new Guid("60000000-0000-0000-0000-00000000001e"));

            migrationBuilder.DeleteData(
                table: "NotificationTemplates",
                keyColumn: "Id",
                keyValue: new Guid("60000000-0000-0000-0000-00000000001f"));
        }
    }
}

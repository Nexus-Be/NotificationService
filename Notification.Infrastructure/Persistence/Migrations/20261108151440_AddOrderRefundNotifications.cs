using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Notification.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderRefundNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OrderRefundDispatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RefundId = table.Column<Guid>(type: "uuid", nullable: false),
                    WalletCreditTransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderKind = table.Column<int>(type: "integer", nullable: false),
                    AuctionId = table.Column<Guid>(type: "uuid", nullable: true),
                    NotificationId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderRefundDispatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderRefundDispatches_Notifications_NotificationId",
                        column: x => x.NotificationId,
                        principalTable: "Notifications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "NotificationTemplates",
                columns: new[] { "Id", "BodyTemplate", "ChannelCode", "Code", "CreatedAt", "CreatedBy", "DeletedAt", "IsActive", "IsDeleted", "IsMandatory", "Locale", "NotificationType", "SubjectTemplate", "UpdatedAt", "UpdatedBy" },
                values: new object[] { new Guid("60000000-0000-0000-0000-000000000020"), "Đơn {{OrderNo}} đã được hoàn {{RefundAmount}} VND vào ví Buyer,ghi nhận lúc {{CompletedAtUtc}}. Vui lòng kiểm tra giao dịch và số dư hiện tại trong Nexus.", 1, "ORDER_REFUND_COMPLETED_PUSH", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("00000000-0000-0000-0000-000000000000"), null, true, false, true, "vi-VN", 13, "Hoàn tiền vào ví Buyer thành công", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("00000000-0000-0000-0000-000000000000") });

            migrationBuilder.CreateIndex(
                name: "IX_OrderRefundDispatches_NotificationId",
                table: "OrderRefundDispatches",
                column: "NotificationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderRefundDispatches_OrderKind_OrderId",
                table: "OrderRefundDispatches",
                columns: new[] { "OrderKind", "OrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderRefundDispatches_RefundId",
                table: "OrderRefundDispatches",
                column: "RefundId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderRefundDispatches_WalletCreditTransactionId",
                table: "OrderRefundDispatches",
                column: "WalletCreditTransactionId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrderRefundDispatches");

            migrationBuilder.DeleteData(
                table: "NotificationTemplates",
                keyColumn: "Id",
                keyValue: new Guid("60000000-0000-0000-0000-000000000020"));
        }
    }
}

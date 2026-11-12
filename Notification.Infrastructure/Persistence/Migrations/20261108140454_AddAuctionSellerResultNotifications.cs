using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Notification.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAuctionSellerResultNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuctionSellerResultDispatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AuctionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResultId = table.Column<Guid>(type: "uuid", nullable: false),
                    WinningBidId = table.Column<Guid>(type: "uuid", nullable: true),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    NotificationId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuctionSellerResultDispatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuctionSellerResultDispatches_Notifications_NotificationId",
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
                    { new Guid("60000000-0000-0000-0000-000000000014"), "Kết quả phiên {{AuctionTitle}} ghi nhận lúc {{DecidedAtUtc}}: đã có người thắng với giá {{WinningAmount}} VND; hạn thanh toán gốc {{PaymentDeadlineUtc}}. Vui lòng kiểm tra phiên và đơn hiện tại trong Nexus.", 1, "AUCTION_SELLER_WINNER_SELECTED_PUSH", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("00000000-0000-0000-0000-000000000000"), null, true, false, false, "vi-VN", 10, "Phiên đấu giá đã có người thắng", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("00000000-0000-0000-0000-000000000000") },
                    { new Guid("60000000-0000-0000-0000-000000000015"), "Phiên {{AuctionTitle}} thất bại vì không có lượt đặt giá, theo kết quả ghi nhận lúc {{DecidedAtUtc}}. Vui lòng kiểm tra chi tiết phiên hiện tại trong Nexus.", 1, "AUCTION_SELLER_NO_BIDS_PUSH", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("00000000-0000-0000-0000-000000000000"), null, true, false, false, "vi-VN", 10, "Phiên đấu giá kết thúc không có bid", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("00000000-0000-0000-0000-000000000000") }
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuctionSellerResultDispatches_AuctionId",
                table: "AuctionSellerResultDispatches",
                column: "AuctionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuctionSellerResultDispatches_NotificationId",
                table: "AuctionSellerResultDispatches",
                column: "NotificationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuctionSellerResultDispatches_OrderId",
                table: "AuctionSellerResultDispatches",
                column: "OrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuctionSellerResultDispatches_ResultId",
                table: "AuctionSellerResultDispatches",
                column: "ResultId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuctionSellerResultDispatches_WinningBidId",
                table: "AuctionSellerResultDispatches",
                column: "WinningBidId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuctionSellerResultDispatches");

            migrationBuilder.DeleteData(
                table: "NotificationTemplates",
                keyColumn: "Id",
                keyValue: new Guid("60000000-0000-0000-0000-000000000014"));

            migrationBuilder.DeleteData(
                table: "NotificationTemplates",
                keyColumn: "Id",
                keyValue: new Guid("60000000-0000-0000-0000-000000000015"));
        }
    }
}

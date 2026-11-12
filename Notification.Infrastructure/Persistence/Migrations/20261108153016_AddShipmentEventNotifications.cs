using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Notification.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddShipmentEventNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ShipmentNotificationStreams",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HeaderHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LastVersion = table.Column<long>(type: "bigint", nullable: false),
                    LastOccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastStatus = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShipmentNotificationStreams", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ShipmentEventDispatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ShipmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceVersion = table.Column<long>(type: "bigint", nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SuppressionReason = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    BuyerNotificationId = table.Column<Guid>(type: "uuid", nullable: true),
                    SellerNotificationId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShipmentEventDispatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShipmentEventDispatches_Notifications_BuyerNotificationId",
                        column: x => x.BuyerNotificationId,
                        principalTable: "Notifications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShipmentEventDispatches_Notifications_SellerNotificationId",
                        column: x => x.SellerNotificationId,
                        principalTable: "Notifications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShipmentEventDispatches_ShipmentNotificationStreams_Shipmen~",
                        column: x => x.ShipmentId,
                        principalTable: "ShipmentNotificationStreams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "NotificationTemplates",
                columns: new[] { "Id", "BodyTemplate", "ChannelCode", "Code", "CreatedAt", "CreatedBy", "DeletedAt", "IsActive", "IsDeleted", "IsMandatory", "Locale", "NotificationType", "SubjectTemplate", "UpdatedAt", "UpdatedBy" },
                values: new object[,]
                {
                    { new Guid("60000000-0000-0000-0000-000000000021"), "Đơn {{OrderNo}},mã vận đơn {{TrackingCode}}: Vận đơn GHN đã được tạo,ghi nhận lúc {{OccurredAtUtc}}. Vui lòng kiểm tra trạng thái đơn và vận đơn hiện tại trong Nexus.", 1, "SHIPMENT_CREATED_BUYER_PUSH", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("00000000-0000-0000-0000-000000000000"), null, true, false, false, "vi-VN", 1, "Cập nhật vận chuyển đơn hàng", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("00000000-0000-0000-0000-000000000000") },
                    { new Guid("60000000-0000-0000-0000-000000000022"), "Đơn {{OrderNo}},mã vận đơn {{TrackingCode}}: Vận đơn GHN đã được tạo,ghi nhận lúc {{OccurredAtUtc}}. Vui lòng kiểm tra trạng thái đơn và vận đơn hiện tại trong Nexus.", 1, "SHIPMENT_CREATED_SELLER_PUSH", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("00000000-0000-0000-0000-000000000000"), null, true, false, false, "vi-VN", 1, "Cập nhật vận chuyển đơn hàng", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("00000000-0000-0000-0000-000000000000") },
                    { new Guid("60000000-0000-0000-0000-000000000023"), "Đơn {{OrderNo}},mã vận đơn {{TrackingCode}}: GHN ghi nhận đã lấy hàng,ghi nhận lúc {{OccurredAtUtc}}. Vui lòng kiểm tra trạng thái đơn và vận đơn hiện tại trong Nexus.", 1, "SHIPMENT_PICKED_UP_BUYER_PUSH", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("00000000-0000-0000-0000-000000000000"), null, true, false, false, "vi-VN", 1, "Cập nhật vận chuyển đơn hàng", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("00000000-0000-0000-0000-000000000000") },
                    { new Guid("60000000-0000-0000-0000-000000000024"), "Đơn {{OrderNo}},mã vận đơn {{TrackingCode}}: GHN ghi nhận đã lấy hàng,ghi nhận lúc {{OccurredAtUtc}}. Vui lòng kiểm tra trạng thái đơn và vận đơn hiện tại trong Nexus.", 1, "SHIPMENT_PICKED_UP_SELLER_PUSH", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("00000000-0000-0000-0000-000000000000"), null, true, false, false, "vi-VN", 1, "Cập nhật vận chuyển đơn hàng", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("00000000-0000-0000-0000-000000000000") },
                    { new Guid("60000000-0000-0000-0000-000000000025"), "Đơn {{OrderNo}},mã vận đơn {{TrackingCode}}: GHN ghi nhận hàng đang được vận chuyển,ghi nhận lúc {{OccurredAtUtc}}. Vui lòng kiểm tra trạng thái đơn và vận đơn hiện tại trong Nexus.", 1, "SHIPMENT_IN_TRANSIT_BUYER_PUSH", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("00000000-0000-0000-0000-000000000000"), null, true, false, false, "vi-VN", 1, "Cập nhật vận chuyển đơn hàng", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("00000000-0000-0000-0000-000000000000") },
                    { new Guid("60000000-0000-0000-0000-000000000026"), "Đơn {{OrderNo}},mã vận đơn {{TrackingCode}}: GHN ghi nhận hàng đang được vận chuyển,ghi nhận lúc {{OccurredAtUtc}}. Vui lòng kiểm tra trạng thái đơn và vận đơn hiện tại trong Nexus.", 1, "SHIPMENT_IN_TRANSIT_SELLER_PUSH", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("00000000-0000-0000-0000-000000000000"), null, true, false, false, "vi-VN", 1, "Cập nhật vận chuyển đơn hàng", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("00000000-0000-0000-0000-000000000000") },
                    { new Guid("60000000-0000-0000-0000-000000000027"), "Đơn {{OrderNo}},mã vận đơn {{TrackingCode}}: GHN ghi nhận đang giao hàng,ghi nhận lúc {{OccurredAtUtc}}. Vui lòng kiểm tra trạng thái đơn và vận đơn hiện tại trong Nexus.", 1, "SHIPMENT_OUT_FOR_DELIVERY_BUYER_PUSH", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("00000000-0000-0000-0000-000000000000"), null, true, false, false, "vi-VN", 1, "Cập nhật vận chuyển đơn hàng", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("00000000-0000-0000-0000-000000000000") },
                    { new Guid("60000000-0000-0000-0000-000000000028"), "Đơn {{OrderNo}},mã vận đơn {{TrackingCode}}: GHN ghi nhận đang giao hàng,ghi nhận lúc {{OccurredAtUtc}}. Vui lòng kiểm tra trạng thái đơn và vận đơn hiện tại trong Nexus.", 1, "SHIPMENT_OUT_FOR_DELIVERY_SELLER_PUSH", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("00000000-0000-0000-0000-000000000000"), null, true, false, false, "vi-VN", 1, "Cập nhật vận chuyển đơn hàng", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("00000000-0000-0000-0000-000000000000") },
                    { new Guid("60000000-0000-0000-0000-000000000029"), "Đơn {{OrderNo}},mã vận đơn {{TrackingCode}}: GHN ghi nhận đã giao hàng thành công,ghi nhận lúc {{OccurredAtUtc}}. Vui lòng kiểm tra trạng thái đơn và vận đơn hiện tại trong Nexus.", 1, "SHIPMENT_DELIVERED_BUYER_PUSH", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("00000000-0000-0000-0000-000000000000"), null, true, false, false, "vi-VN", 1, "Cập nhật vận chuyển đơn hàng", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("00000000-0000-0000-0000-000000000000") },
                    { new Guid("60000000-0000-0000-0000-00000000002a"), "Đơn {{OrderNo}},mã vận đơn {{TrackingCode}}: GHN ghi nhận đã giao hàng thành công,ghi nhận lúc {{OccurredAtUtc}}. Vui lòng kiểm tra trạng thái đơn và vận đơn hiện tại trong Nexus.", 1, "SHIPMENT_DELIVERED_SELLER_PUSH", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("00000000-0000-0000-0000-000000000000"), null, true, false, false, "vi-VN", 1, "Cập nhật vận chuyển đơn hàng", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("00000000-0000-0000-0000-000000000000") },
                    { new Guid("60000000-0000-0000-0000-00000000002b"), "Đơn {{OrderNo}},mã vận đơn {{TrackingCode}}: GHN ghi nhận giao hàng không thành công,ghi nhận lúc {{OccurredAtUtc}}. Lý do: {{Reason}}. Vui lòng kiểm tra trạng thái đơn và vận đơn hiện tại trong Nexus.", 1, "SHIPMENT_DELIVERY_FAILED_BUYER_PUSH", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("00000000-0000-0000-0000-000000000000"), null, true, false, false, "vi-VN", 1, "Cập nhật vận chuyển đơn hàng", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("00000000-0000-0000-0000-000000000000") },
                    { new Guid("60000000-0000-0000-0000-00000000002c"), "Đơn {{OrderNo}},mã vận đơn {{TrackingCode}}: GHN ghi nhận giao hàng không thành công,ghi nhận lúc {{OccurredAtUtc}}. Lý do: {{Reason}}. Vui lòng kiểm tra trạng thái đơn và vận đơn hiện tại trong Nexus.", 1, "SHIPMENT_DELIVERY_FAILED_SELLER_PUSH", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("00000000-0000-0000-0000-000000000000"), null, true, false, false, "vi-VN", 1, "Cập nhật vận chuyển đơn hàng", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("00000000-0000-0000-0000-000000000000") },
                    { new Guid("60000000-0000-0000-0000-00000000002d"), "Đơn {{OrderNo}},mã vận đơn {{TrackingCode}}: GHN ghi nhận hàng đang chuyển hoàn,ghi nhận lúc {{OccurredAtUtc}}. Lý do: {{Reason}}. Vui lòng kiểm tra trạng thái đơn và vận đơn hiện tại trong Nexus.", 1, "SHIPMENT_RETURNING_BUYER_PUSH", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("00000000-0000-0000-0000-000000000000"), null, true, false, false, "vi-VN", 1, "Cập nhật vận chuyển đơn hàng", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("00000000-0000-0000-0000-000000000000") },
                    { new Guid("60000000-0000-0000-0000-00000000002e"), "Đơn {{OrderNo}},mã vận đơn {{TrackingCode}}: GHN ghi nhận hàng đang chuyển hoàn,ghi nhận lúc {{OccurredAtUtc}}. Lý do: {{Reason}}. Vui lòng kiểm tra trạng thái đơn và vận đơn hiện tại trong Nexus.", 1, "SHIPMENT_RETURNING_SELLER_PUSH", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("00000000-0000-0000-0000-000000000000"), null, true, false, false, "vi-VN", 1, "Cập nhật vận chuyển đơn hàng", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("00000000-0000-0000-0000-000000000000") },
                    { new Guid("60000000-0000-0000-0000-00000000002f"), "Đơn {{OrderNo}},mã vận đơn {{TrackingCode}}: GHN ghi nhận hàng đã chuyển hoàn,ghi nhận lúc {{OccurredAtUtc}}. Lý do: {{Reason}}. Vui lòng kiểm tra trạng thái đơn và vận đơn hiện tại trong Nexus.", 1, "SHIPMENT_RETURNED_BUYER_PUSH", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("00000000-0000-0000-0000-000000000000"), null, true, false, false, "vi-VN", 1, "Cập nhật vận chuyển đơn hàng", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("00000000-0000-0000-0000-000000000000") },
                    { new Guid("60000000-0000-0000-0000-000000000030"), "Đơn {{OrderNo}},mã vận đơn {{TrackingCode}}: GHN ghi nhận hàng đã chuyển hoàn,ghi nhận lúc {{OccurredAtUtc}}. Lý do: {{Reason}}. Vui lòng kiểm tra trạng thái đơn và vận đơn hiện tại trong Nexus.", 1, "SHIPMENT_RETURNED_SELLER_PUSH", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("00000000-0000-0000-0000-000000000000"), null, true, false, false, "vi-VN", 1, "Cập nhật vận chuyển đơn hàng", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("00000000-0000-0000-0000-000000000000") },
                    { new Guid("60000000-0000-0000-0000-000000000031"), "Đơn {{OrderNo}},mã vận đơn {{TrackingCode}}: GHN ghi nhận vận đơn đã bị hủy,ghi nhận lúc {{OccurredAtUtc}}. Lý do: {{Reason}}. Vui lòng kiểm tra trạng thái đơn và vận đơn hiện tại trong Nexus.", 1, "SHIPMENT_CANCELLED_BUYER_PUSH", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("00000000-0000-0000-0000-000000000000"), null, true, false, false, "vi-VN", 1, "Cập nhật vận chuyển đơn hàng", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("00000000-0000-0000-0000-000000000000") },
                    { new Guid("60000000-0000-0000-0000-000000000032"), "Đơn {{OrderNo}},mã vận đơn {{TrackingCode}}: GHN ghi nhận vận đơn đã bị hủy,ghi nhận lúc {{OccurredAtUtc}}. Lý do: {{Reason}}. Vui lòng kiểm tra trạng thái đơn và vận đơn hiện tại trong Nexus.", 1, "SHIPMENT_CANCELLED_SELLER_PUSH", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("00000000-0000-0000-0000-000000000000"), null, true, false, false, "vi-VN", 1, "Cập nhật vận chuyển đơn hàng", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("00000000-0000-0000-0000-000000000000") }
                });

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentEventDispatches_BuyerNotificationId",
                table: "ShipmentEventDispatches",
                column: "BuyerNotificationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentEventDispatches_SellerNotificationId",
                table: "ShipmentEventDispatches",
                column: "SellerNotificationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentEventDispatches_ShipmentId_SourceVersion",
                table: "ShipmentEventDispatches",
                columns: new[] { "ShipmentId", "SourceVersion" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ShipmentEventDispatches");

            migrationBuilder.DropTable(
                name: "ShipmentNotificationStreams");

            migrationBuilder.DeleteData(
                table: "NotificationTemplates",
                keyColumn: "Id",
                keyValue: new Guid("60000000-0000-0000-0000-000000000021"));

            migrationBuilder.DeleteData(
                table: "NotificationTemplates",
                keyColumn: "Id",
                keyValue: new Guid("60000000-0000-0000-0000-000000000022"));

            migrationBuilder.DeleteData(
                table: "NotificationTemplates",
                keyColumn: "Id",
                keyValue: new Guid("60000000-0000-0000-0000-000000000023"));

            migrationBuilder.DeleteData(
                table: "NotificationTemplates",
                keyColumn: "Id",
                keyValue: new Guid("60000000-0000-0000-0000-000000000024"));

            migrationBuilder.DeleteData(
                table: "NotificationTemplates",
                keyColumn: "Id",
                keyValue: new Guid("60000000-0000-0000-0000-000000000025"));

            migrationBuilder.DeleteData(
                table: "NotificationTemplates",
                keyColumn: "Id",
                keyValue: new Guid("60000000-0000-0000-0000-000000000026"));

            migrationBuilder.DeleteData(
                table: "NotificationTemplates",
                keyColumn: "Id",
                keyValue: new Guid("60000000-0000-0000-0000-000000000027"));

            migrationBuilder.DeleteData(
                table: "NotificationTemplates",
                keyColumn: "Id",
                keyValue: new Guid("60000000-0000-0000-0000-000000000028"));

            migrationBuilder.DeleteData(
                table: "NotificationTemplates",
                keyColumn: "Id",
                keyValue: new Guid("60000000-0000-0000-0000-000000000029"));

            migrationBuilder.DeleteData(
                table: "NotificationTemplates",
                keyColumn: "Id",
                keyValue: new Guid("60000000-0000-0000-0000-00000000002a"));

            migrationBuilder.DeleteData(
                table: "NotificationTemplates",
                keyColumn: "Id",
                keyValue: new Guid("60000000-0000-0000-0000-00000000002b"));

            migrationBuilder.DeleteData(
                table: "NotificationTemplates",
                keyColumn: "Id",
                keyValue: new Guid("60000000-0000-0000-0000-00000000002c"));

            migrationBuilder.DeleteData(
                table: "NotificationTemplates",
                keyColumn: "Id",
                keyValue: new Guid("60000000-0000-0000-0000-00000000002d"));

            migrationBuilder.DeleteData(
                table: "NotificationTemplates",
                keyColumn: "Id",
                keyValue: new Guid("60000000-0000-0000-0000-00000000002e"));

            migrationBuilder.DeleteData(
                table: "NotificationTemplates",
                keyColumn: "Id",
                keyValue: new Guid("60000000-0000-0000-0000-00000000002f"));

            migrationBuilder.DeleteData(
                table: "NotificationTemplates",
                keyColumn: "Id",
                keyValue: new Guid("60000000-0000-0000-0000-000000000030"));

            migrationBuilder.DeleteData(
                table: "NotificationTemplates",
                keyColumn: "Id",
                keyValue: new Guid("60000000-0000-0000-0000-000000000031"));

            migrationBuilder.DeleteData(
                table: "NotificationTemplates",
                keyColumn: "Id",
                keyValue: new Guid("60000000-0000-0000-0000-000000000032"));
        }
    }
}

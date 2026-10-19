namespace Notification.Infrastructure.Persistence.Seeds;

using Microsoft.EntityFrameworkCore;
using Notification.Domain.Entities;
using Notification.Domain.Enums;

public static class NotificationSeedData
{
    public static void SeedChannels(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<NotificationChannel>().HasData(
            new NotificationChannel
            {
                Id = Guid.Parse("10101010-1010-1010-1010-101010101010"),
                Code = ChannelType.Email,
                Name = "Thư điện tử (Email)",
                Provider = ChannelProvider.Smtp,
                IsDisabled = false,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                IsDeleted = false
            },
            new NotificationChannel
            {
                Id = Guid.Parse("20202020-2020-2020-2020-202020202020"),
                Code = ChannelType.Push,
                Name = "Thông báo đẩy di động (Push)",
                Provider = ChannelProvider.Fcm,
                IsDisabled = false,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                IsDeleted = false
            },
            new NotificationChannel
            {
                Id = Guid.Parse("30303030-3030-3030-3030-303030303030"),
                Code = ChannelType.InApp,
                Name = "Thông báo trung tâm ứng dụng (InApp)",
                IsDisabled = false,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                IsDeleted = false
            },
            new NotificationChannel
            {
                Id = Guid.Parse("40404040-4040-4040-4040-404040404040"),
                Code = ChannelType.Sms,
                Name = "Tin nhắn viễn thông (SMS)",
                IsDisabled = false,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                IsDeleted = false
            }
        );
    }
}

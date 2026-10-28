
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Notification.Application.Interfaces;
using Notification.Domain.Entities;
using Notification.Domain.Enums;

namespace Notification.Application.Services.Notifications;

public sealed class NotificationTemplateService
    : INotificationTemplateService
{
    private readonly INotificationDbContext _db;

    public NotificationTemplateService(
        INotificationDbContext db)
    {
        _db = db;
    }

    // NOTI-08: Get notification templates
    public async Task<NotificationTemplatePageDto> GetAsync(
        ChannelType? channelCode,
        NotificationType? notificationType,
        int pageIndex,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        if (pageIndex < 1 || pageSize < 1 || pageSize > 100)
        {
            throw new ArgumentException(
                "Invalid pagination parameters.");
        }

        if ((channelCode.HasValue &&
             !Enum.IsDefined(channelCode.Value)) ||
            (notificationType.HasValue &&
             !Enum.IsDefined(notificationType.Value)))
        {
            throw new ArgumentException(
                "Invalid template filters.");
        }

        var offset = ((long)pageIndex - 1) * pageSize;

        if (offset > int.MaxValue)
        {
            throw new ArgumentException(
                "Requested page exceeds supported range.");
        }

        var query = _db.NotificationTemplates
            .AsNoTracking()
            .Where(t => !t.IsDeleted);

        if (channelCode.HasValue)
        {
            query = query.Where(t =>
                t.ChannelCode == channelCode.Value);
        }

        if (notificationType.HasValue)
        {
            query = query.Where(t =>
                t.NotificationType == notificationType.Value);
        }

        var total = await query.CountAsync(
            cancellationToken);

        var items = await query
            .OrderBy(t => t.Code)
            .ThenBy(t => t.Locale)
            .ThenBy(t => t.Id)
            .Skip((int)offset)
            .Take(pageSize)
            .Select(t => new NotificationTemplateDto(
                t.Id,
                t.Code,
                t.NotificationType,
                t.ChannelCode,
                t.SubjectTemplate,
                t.BodyTemplate,
                t.IsMandatory,
                t.Locale,
                t.IsActive))
            .ToListAsync(cancellationToken);

        return new NotificationTemplatePageDto(
            items,
            total,
            pageIndex,
            pageSize);
    }

    // NOTI-08: Create notification template
    public async Task<NotificationTemplateDto> CreateAsync(
        CreateNotificationTemplateDto request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // 1. Validate enum values
        if (!Enum.IsDefined(request.NotificationType) ||
            !Enum.IsDefined(request.ChannelCode))
        {
            throw new ArgumentException(
                "Invalid notification type or channel.");
        }

        // 2. Validate template code
        var code = request.Code?.Trim().ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(code) ||
            code.Length > 150 ||
            !Regex.IsMatch(code, @"^[A-Z][A-Z0-9_]*$"))
        {
            throw new ArgumentException(
                "Invalid template code.");
        }

        // 3. Validate locale
        var locale = request.Locale?.Trim();

        if (string.IsNullOrWhiteSpace(locale) ||
            locale.Length > 20 ||
            !Regex.IsMatch(
                locale,
                @"^[a-z]{2,3}(-[A-Za-z0-9]{2,8})*$"))
        {
            throw new ArgumentException(
                "Invalid template locale.");
        }

        // 4. Validate template content
        if (string.IsNullOrWhiteSpace(request.BodyTemplate) ||
            request.BodyTemplate.Length > 4000)
        {
            throw new ArgumentException(
                "Body template must contain 1 to 4000 characters.");
        }

        if (request.SubjectTemplate?.Length > 500)
        {
            throw new ArgumentException(
                "Subject template exceeds 500 characters.");
        }

        ValidateTemplateSyntax(request.SubjectTemplate);
        ValidateTemplateSyntax(request.BodyTemplate);

        // 5. Validate channel existence
        var channelExists = await _db.NotificationChannels
            .AsNoTracking()
            .AnyAsync(
                c => c.Code == request.ChannelCode
                     && !c.IsDeleted,
                cancellationToken);

        if (!channelExists)
        {
            throw new ArgumentException(
                "Notification channel does not exist.");
        }

        // 6. Check duplicate code + locale
        var duplicated = await _db.NotificationTemplates
            .AsNoTracking()
            .AnyAsync(
                t => t.Code == code &&
                     t.Locale == locale,
                cancellationToken);

        if (duplicated)
        {
            throw new InvalidOperationException(
                "TEMPLATE_CODE_DUPLICATED");
        }

        // 7. Create entity
        var now = DateTimeOffset.UtcNow;

        var template = new NotificationTemplate
        {
            Id = Guid.NewGuid(),
            Code = code,
            NotificationType = request.NotificationType,
            ChannelCode = request.ChannelCode,
            SubjectTemplate = request.SubjectTemplate,
            BodyTemplate = request.BodyTemplate,
            IsMandatory = request.IsMandatory,
            Locale = locale,
            IsActive = request.IsActive,
            CreatedAt = now,
            UpdatedAt = now
        };

        _db.NotificationTemplates.Add(template);

        // 8. Save database
        await _db.SaveChangesAsync(cancellationToken);

        // 9. Return DTO
        return new NotificationTemplateDto(
            template.Id,
            template.Code,
            template.NotificationType,
            template.ChannelCode,
            template.SubjectTemplate,
            template.BodyTemplate,
            template.IsMandatory,
            template.Locale,
            template.IsActive);
    }

    // Validate {{variableName}} placeholders
    private static void ValidateTemplateSyntax(string? content)
    {
        if (string.IsNullOrEmpty(content))
            return;

        const string pattern =
            @"\{\{\s*[a-zA-Z][a-zA-Z0-9_.]*\s*\}\}";

        var remaining = Regex.Replace(
            content,
            pattern,
            "");

        if (remaining.Contains("{{") ||
            remaining.Contains("}}"))
        {
            throw new ArgumentException(
                "INVALID_TEMPLATE_SYNTAX");
        }
    }
}

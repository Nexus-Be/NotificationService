namespace Notification.Application.Services.Management;

using System.Data.Common;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Notification.Application.Common.Extensions;
using Notification.Application.Common.Models;
using Notification.Application.Interfaces;
using Notification.Application.Services.Dispatches;
using Notification.Domain.Entities;
using Notification.Domain.Enums;
using Notification.Domain.Exceptions;

public sealed partial class NotificationManagementService : INotificationManagementService
{
    private readonly INotificationDbContext _db;
    private readonly TimeProvider _time;
    public NotificationManagementService(INotificationDbContext db, TimeProvider time) { _db = db; _time = time; }

    public async Task<PagedData<TemplateResponse>> ListTemplatesAsync(TemplateListRequest request, CancellationToken cancellationToken = default)
    {
        var skip = PageOffset(request);
        var query = _db.NotificationTemplates.AsNoTracking().Where(t => !t.IsDeleted);
        if (!string.IsNullOrWhiteSpace(request.Code)) query = query.Where(t => t.Code.Contains(request.Code.Trim()));
        if (!string.IsNullOrWhiteSpace(request.Channel)) { var code = EnumValue<ChannelType>(request.Channel); query = query.Where(t => t.ChannelCode == code); }
        if (!string.IsNullOrWhiteSpace(request.NotificationType)) { var type = EnumValue<NotificationType>(request.NotificationType); query = query.Where(t => t.NotificationType == type); }
        if (request.IsActive.HasValue) query = query.Where(t => t.IsActive == request.IsActive.Value);
        var count = await query.CountAsync(cancellationToken);
        var rows = await query.OrderBy(t => t.Code).ThenBy(t => t.Locale).ThenBy(t => t.Id).Skip(skip).Take(request.PageSize).ToListAsync(cancellationToken);
        return new(rows.Select(TemplateDto).ToList(), PaginationMetadata.Create(request.Page, request.PageSize, count));
    }

    public async Task<TemplateResponse> GetTemplateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        ValidId(id);
        var row = await _db.NotificationTemplates.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id && !t.IsDeleted, cancellationToken);
        return row is null ? throw Error("TEMPLATE_NOT_FOUND", "Notification template was not found.", 404) : TemplateDto(row);
    }

    public async Task<TemplateResponse> CreateTemplateAsync(CreateTemplateRequest request, ManagementActor actor, CancellationToken cancellationToken = default)
    {
        ValidActor(actor);
        if (string.IsNullOrWhiteSpace(request.Code) || request.Code.Trim().Length > 150 || string.IsNullOrWhiteSpace(request.Locale) || request.Locale.Trim().Length > 20)
            throw Error("VAL_INVALID_INPUT", "Template code or locale is invalid.");
        var type = EnumValue<NotificationType>(request.NotificationType);
        var channel = EnumValue<ChannelType>(request.ChannelCode);
        ValidateContent(request.SubjectTemplate, request.BodyTemplate, type, request.IsMandatory);
        if (!await _db.NotificationChannels.AsNoTracking().AnyAsync(c => c.Code == channel && !c.IsDeleted, cancellationToken))
            throw Error("CHANNEL_NOT_FOUND", "Notification channel was not found.", 404);
        var code = request.Code.Trim(); var locale = request.Locale.Trim();
        if (await _db.NotificationTemplates.IgnoreQueryFilters().AsNoTracking().AnyAsync(t => t.Code == code && t.Locale == locale, cancellationToken))
            throw Error("TEMPLATE_CONFLICT", "Template code and locale already exist.", 409);
        var now = NextTime(DateTimeOffset.MinValue);
        var row = new NotificationTemplate
        {
            Id = Guid.NewGuid(), Code = code, Locale = locale, NotificationType = type, ChannelCode = channel,
            SubjectTemplate = request.SubjectTemplate, BodyTemplate = request.BodyTemplate,
            IsMandatory = request.IsMandatory, IsActive = request.IsActive, CreatedAt = now, UpdatedAt = now,
            CreatedBy = actor.UserId, UpdatedBy = actor.UserId
        };
        _db.NotificationTemplates.Add(row);
        var audit = Audit(AuditAction.TemplateCreate, nameof(NotificationTemplate), row.Id, null, TemplateDto(row), actor);
        try { await _db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException ex) when (UniqueViolation(ex))
        {
            _db.NotificationTemplates.Remove(row); _db.AuditLogs.Remove(audit);
            throw Error("TEMPLATE_CONFLICT", "Template code and locale already exist.", 409);
        }
        return TemplateDto(row);
    }

    public async Task<TemplateResponse> UpdateTemplateAsync(Guid id, UpdateTemplateRequest request, ManagementActor actor, CancellationToken cancellationToken = default)
    {
        ValidId(id); ValidActor(actor);
        if (request.ExpectedUpdatedAt is null || request.IsActive is null || request.IsMandatory is null)
            throw Error("VAL_INVALID_INPUT", "Template version and boolean settings are required.");
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        var rows = await _db.NotificationTemplates.FromSqlInterpolated($"SELECT * FROM \"NotificationTemplates\" WHERE \"Id\" = {id} FOR UPDATE")
            .IgnoreQueryFilters().AsNoTracking().ToListAsync(cancellationToken);
        var row = rows.SingleOrDefault();
        if (row is null || row.IsDeleted) throw Error("TEMPLATE_NOT_FOUND", "Notification template was not found.", 404);
        if (row.UpdatedAt != request.ExpectedUpdatedAt.Value.ToUniversalTime()) throw Error("CONFIG_VERSION_CONFLICT", "Template settings changed; reload before updating.", 409);
        ValidateContent(request.SubjectTemplate, request.BodyTemplate, row.NotificationType, request.IsMandatory.Value);
        var before = TemplateDto(row); var now = NextTime(row.UpdatedAt);
        await _db.NotificationTemplates.Where(t => t.Id == id).ExecuteUpdateAsync(set => set
            .SetProperty(t => t.SubjectTemplate, request.SubjectTemplate).SetProperty(t => t.BodyTemplate, request.BodyTemplate)
            .SetProperty(t => t.IsMandatory, request.IsMandatory.Value).SetProperty(t => t.IsActive, request.IsActive.Value)
            .SetProperty(t => t.UpdatedAt, now).SetProperty(t => t.UpdatedBy, actor.UserId), cancellationToken);
        row.SubjectTemplate = request.SubjectTemplate; row.BodyTemplate = request.BodyTemplate;
        row.IsMandatory = request.IsMandatory.Value; row.IsActive = request.IsActive.Value; row.UpdatedAt = now;
        Audit(AuditAction.TemplateUpdate, nameof(NotificationTemplate), id, before, TemplateDto(row), actor);
        await _db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);
        return TemplateDto(row);
    }

    public async Task<PagedData<ChannelResponse>> ListChannelsAsync(ManagementPageRequest request, CancellationToken cancellationToken = default)
    {
        var skip = PageOffset(request);
        var query = _db.NotificationChannels.AsNoTracking().Where(c => !c.IsDeleted);
        var count = await query.CountAsync(cancellationToken);
        var rows = await query.OrderBy(c => c.Code).Skip(skip).Take(request.PageSize).ToListAsync(cancellationToken);
        return new(rows.Select(ChannelDto).ToList(), PaginationMetadata.Create(request.Page, request.PageSize, count));
    }

    public async Task<ChannelResponse> UpdateChannelAsync(string code, UpdateChannelRequest request, ManagementActor actor, CancellationToken cancellationToken = default)
    {
        ValidActor(actor); var channel = EnumValue<ChannelType>(code);
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 150 || request.IsDisabled is null || request.ExpectedUpdatedAt is null)
            throw Error("VAL_INVALID_INPUT", "Channel name, disabled setting and version are required.");
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        var rows = await _db.NotificationChannels.FromSqlInterpolated($"SELECT * FROM \"NotificationChannels\" WHERE \"Code\" = {(int)channel} FOR UPDATE")
            .IgnoreQueryFilters().AsNoTracking().ToListAsync(cancellationToken);
        var row = rows.SingleOrDefault();
        if (row is null || row.IsDeleted) throw Error("CHANNEL_NOT_FOUND", "Notification channel was not found.", 404);
        if (row.UpdatedAt != request.ExpectedUpdatedAt.Value.ToUniversalTime()) throw Error("CONFIG_VERSION_CONFLICT", "Channel settings changed; reload before updating.", 409);
        var before = ChannelDto(row); var now = NextTime(row.UpdatedAt); var name = request.Name.Trim();
        await _db.NotificationChannels.Where(c => c.Id == row.Id).ExecuteUpdateAsync(set => set.SetProperty(c => c.Name, name)
            .SetProperty(c => c.IsDisabled, request.IsDisabled.Value).SetProperty(c => c.UpdatedAt, now).SetProperty(c => c.UpdatedBy, actor.UserId), cancellationToken);
        row.Name = name; row.IsDisabled = request.IsDisabled.Value; row.UpdatedAt = now;
        Audit(AuditAction.ChannelUpdate, nameof(NotificationChannel), row.Id, before, ChannelDto(row), actor);
        await _db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);
        return ChannelDto(row);
    }

    private IQueryable<NotificationDelivery> DeliveryQuery() => _db.NotificationDeliveries.IgnoreQueryFilters().AsNoTracking()
        .Where(d => !d.IsDeleted && d.Notification != null).Include(d => d.Notification).ThenInclude(n => n!.Template);

    public async Task<PagedData<DeliveryResponse>> ListDeliveriesAsync(DeliveryListRequest request, CancellationToken cancellationToken = default)
    {
        var skip = PageOffset(request); var query = DeliveryQuery();
        if (request.UserId == Guid.Empty || request.NotificationId == Guid.Empty || request.From > request.To)
            throw Error("VAL_INVALID_INPUT", "Delivery filters contain invalid IDs or date range.");
        if (request.UserId.HasValue) query = query.Where(d => d.Notification!.UserId == request.UserId.Value);
        if (request.NotificationId.HasValue) query = query.Where(d => d.NotificationId == request.NotificationId.Value);
        if (!string.IsNullOrWhiteSpace(request.Channel)) { var channel = EnumValue<ChannelType>(request.Channel); query = query.Where(d => d.ChannelCode == channel); }
        if (!string.IsNullOrWhiteSpace(request.Status)) { var status = EnumValue<DeliveryStatus>(request.Status); query = query.Where(d => d.Status == status); }
        if (request.From.HasValue) { var from = request.From.Value.ToUniversalTime(); query = query.Where(d => d.CreatedAt >= from); }
        if (request.To.HasValue) { var to = request.To.Value.ToUniversalTime(); query = query.Where(d => d.CreatedAt <= to); }
        var count = await query.CountAsync(cancellationToken);
        var rows = await query.OrderByDescending(d => d.CreatedAt).ThenByDescending(d => d.Id).Skip(skip).Take(request.PageSize).ToListAsync(cancellationToken);
        return new(rows.Select(DeliveryDto).ToList(), PaginationMetadata.Create(request.Page, request.PageSize, count));
    }

    public async Task<DeliveryDetailResponse> GetDeliveryAsync(Guid id, CancellationToken cancellationToken = default)
    {
        ValidId(id);
        var row = await DeliveryQuery().SingleOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (row is null) throw Error("DELIVERY_NOT_FOUND", "Notification delivery was not found.", 404);
        return new(DeliveryDto(row), row.Notification!.Status.ToString(), row.Notification.Template?.Code);
    }

    public async Task<PagedData<DeliveryLogResponse>> ListLogsAsync(Guid id, ManagementPageRequest request, CancellationToken cancellationToken = default)
    {
        ValidId(id); var skip = PageOffset(request);
        if (!await _db.NotificationDeliveries.IgnoreQueryFilters().AsNoTracking().AnyAsync(d => d.Id == id && !d.IsDeleted, cancellationToken))
            throw Error("DELIVERY_NOT_FOUND", "Notification delivery was not found.", 404);
        var query = _db.NotificationLogs.AsNoTracking().Where(l => l.DeliveryId == id && !l.IsDeleted);
        var count = await query.CountAsync(cancellationToken);
        var rows = await query.OrderByDescending(l => l.CreatedAt).ThenByDescending(l => l.Id).Skip(skip).Take(request.PageSize).ToListAsync(cancellationToken);
        return new(rows.Select(LogDto).ToList(), PaginationMetadata.Create(request.Page, request.PageSize, count));
    }

    private static TemplateResponse TemplateDto(NotificationTemplate t) => new(t.Id,t.Code,t.Locale,t.NotificationType.ToString(),t.ChannelCode.ToString(),t.SubjectTemplate,t.BodyTemplate,t.IsMandatory,t.IsActive,t.UpdatedAt);
    private static ChannelResponse ChannelDto(NotificationChannel c) => new(c.Id,c.Code.ToString(),c.Name,c.Provider?.ToString(),c.IsDisabled,c.UpdatedAt);
    private static DeliveryResponse DeliveryDto(NotificationDelivery d) => new(d.Id,d.NotificationId,d.Notification!.UserId,d.ChannelCode.ToString(),d.Status.ToString(),
        d.AttemptCount,d.LastAttemptAt,d.NextRetryAt,SafeCode(d.ErrorCode),d.RowVersion,DestinationHint(d),d.CreatedAt,d.UpdatedAt);

    private static string DestinationHint(NotificationDelivery delivery)
    {
        if (delivery.ChannelCode == ChannelType.Push && Guid.TryParse(delivery.Destination, out var device)) return "Device " + device;
        if (delivery.ChannelCode == ChannelType.InApp) return "User " + delivery.Notification!.UserId;
        if (delivery.ChannelCode == ChannelType.Email)
        {
            var at = delivery.Destination.LastIndexOf('@');
            var domain = at >= 0 ? delivery.Destination[(at+1)..] : string.Empty;
            if (domain.Length is > 0 and <= 253 && Regex.IsMatch(domain, @"^[A-Za-z0-9.-]+$")) return "***@" + domain;
        }
        return "Destination hidden";
    }

    private static DeliveryLogResponse LogDto(NotificationLog log)
    {
        int? attempt = null; string? code = null, provider = null; DateTimeOffset? next = null; Guid? requestId = null;
        try
        {
            using var json = JsonDocument.Parse(log.Metadata ?? "{}"); var root = json.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("Attempt", out var a) && a.ValueKind == JsonValueKind.Number && a.TryGetInt32(out var number) && number >= 0) attempt = number;
                if (root.TryGetProperty("RequestId", out var r) && r.ValueKind == JsonValueKind.String && r.TryGetGuid(out var guid)) requestId = guid;
                if (root.TryGetProperty("Details", out var details) && details.ValueKind == JsonValueKind.Object)
                {
                    if (details.TryGetProperty("ErrorCode", out var e) && e.ValueKind == JsonValueKind.String) code = SafeCode(e.GetString());
                    if (details.TryGetProperty("NextRetryAt", out var n) && n.ValueKind == JsonValueKind.String && n.TryGetDateTimeOffset(out var date)) next = date;
                    if (details.TryGetProperty("ProviderMessageId", out var p) && p.ValueKind == JsonValueKind.String)
                    {
                        var value = p.GetString();
                        if (value is { Length: > 0 and <= 500 } && Regex.IsMatch(value, @"^[A-Za-z0-9._:/@-]+$")) provider = value;
                    }
                }
            }
        }
        catch (JsonException) { }
        var message = requestId.HasValue ? "Manual retry accepted." : log.Action switch
        {
            LogAction.Created => "Delivery created.", LogAction.Rendered => "Template rendered.", LogAction.Dispatching => "Delivery attempt started.",
            LogAction.Sent => "Provider accepted the delivery.", LogAction.Failed => "Delivery failed.", LogAction.Retry => "Delivery retry scheduled.",
            LogAction.Suppressed => "Delivery was suppressed.", _ => "Delivery activity recorded."
        };
        return new(log.Id,log.Action.ToString(),log.Level.ToString(),message,attempt,code,next,provider,requestId,log.CreatedAt);
    }

    private AuditLog Audit(AuditAction action, string entity, Guid id, object? before, object? after, ManagementActor actor)
        => _db.AddAuditLog(action,entity,id.ToString(),before,after,userId:actor.UserId.ToString(),traceId:Limit(actor.TraceId,100),ipAddress:Limit(actor.IpAddress,50),userAgent:Limit(actor.UserAgent,500));
    private static string? Limit(string? s,int length) => s is null || s.Length <= length ? s : s[..length];
    private static string? SafeCode(string? s) => s is null ? null : s.Length <= 100 && Regex.IsMatch(s,@"^[A-Z][A-Z0-9_]*$") ? s : "DELIVERY_FAILED";
    private static DomainException Error(string code,string message,int status=400) => new(code,message,status);
    private static void ValidActor(ManagementActor actor) { if (actor.UserId == Guid.Empty) throw Error("UNAUTHORIZED","Authentication is required.",401); }
    private static void ValidId(Guid id) { if (id == Guid.Empty) throw Error("VAL_INVALID_INPUT","ID must not be empty."); }
    private static bool UniqueViolation(DbUpdateException exception) => exception.InnerException is DbException { SqlState: "23505" };
    private static int PageOffset(ManagementPageRequest r)
    {
        if (r.Page < 1 || r.PageSize is < 1 or > 100 || (long)(r.Page-1)*r.PageSize > int.MaxValue)
            throw Error("VAL_INVALID_INPUT","Pagination is outside the supported range.");
        return (r.Page-1)*r.PageSize;
    }
    private static T EnumValue<T>(string? value) where T:struct,Enum
    {
        var name = Enum.GetNames<T>().FirstOrDefault(n => string.Equals(n,value?.Trim(),StringComparison.OrdinalIgnoreCase));
        return name is null ? throw Error("VAL_INVALID_INPUT","Enum value is invalid.") : Enum.Parse<T>(name);
    }
    private static void ValidateContent(string subject,string body,NotificationType type,bool mandatory)
    {
        if (!mandatory && type is NotificationType.OrderPaid or NotificationType.SecurityAlert or NotificationType.Otp)
            throw Error("MANDATORY_TEMPLATE_REQUIRED","Critical notification templates must remain mandatory.");
        try { TemplateRenderer.ValidateTemplate(subject,500); TemplateRenderer.ValidateTemplate(body,4000); }
        catch (DomainException ex) when (ex.ErrorCode == "TEMPLATE_INVALID") { throw Error("VAL_INVALID_TEMPLATE",ex.Message); }
    }
    private DateTimeOffset NextTime(DateTimeOffset previous)
    {
        var now = _time.GetUtcNow().ToUniversalTime(); now = new DateTimeOffset(now.Ticks/10*10,TimeSpan.Zero);
        return now > previous ? now : previous.AddTicks(10);
    }
}

namespace Notification.Application.Services.Dispatches;

using System.ComponentModel.DataAnnotations;

public sealed record CreateDispatchRequest
{
    [Required(ErrorMessage = "Source event ID is required.")]
    public Guid? SourceEventId { get; init; }
    [Required(ErrorMessage = "User ID is required.")]
    public Guid? UserId { get; init; }
    [Required(ErrorMessage = "Template code is required.")]
    [StringLength(150, ErrorMessage = "Template code must not exceed 150 characters.")]
    public string TemplateCode { get; init; } = string.Empty;
    [Required(ErrorMessage = "Locale is required.")]
    [StringLength(20, ErrorMessage = "Locale must not exceed 20 characters.")]
    public string Locale { get; init; } = "vi-VN";
    [Required(ErrorMessage = "Template data is required.")]
    public Dictionary<string, string> Data { get; init; } = new();
    [EmailAddress(ErrorMessage = "Destination must be a valid email address.")]
    [StringLength(500, ErrorMessage = "Destination must not exceed 500 characters.")]
    public string? Destination { get; init; }
}

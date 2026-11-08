namespace Notification.Application.Services.Dispatches;

using System.Net;
using System.Text.RegularExpressions;
using Notification.Domain.Exceptions;

public static class TemplateRenderer
{
    private static readonly Regex Placeholder = new(@"\{\{\s*([A-Za-z][A-Za-z0-9_]*)\s*\}\}", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    public static void ValidateTemplate(string? template, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(template) || template.Length > maxLength)
            throw new DomainException("TEMPLATE_INVALID", "Template content is empty or exceeds the allowed length.", 409);
        var literal = Placeholder.Replace(template, string.Empty);
        if (literal.Contains("{{") || literal.Contains("}}"))
            throw new DomainException("TEMPLATE_INVALID", "Template contains an invalid placeholder.", 409);
    }

    public static string Render(string? template, IReadOnlyDictionary<string, string> data, bool htmlEncode, int maxLength)
    {
        ValidateTemplate(template, maxLength);
        // Regex.Replace evaluates only the original template, never placeholders in substituted data.
        var rendered = Placeholder.Replace(template!, match =>
        {
            if (!data.TryGetValue(match.Groups[1].Value, out var value) || value is null)
                throw new DomainException("TEMPLATE_DATA_MISSING", "Template data is missing a required value.");
            return htmlEncode ? WebUtility.HtmlEncode(value) : value;
        });
        if (rendered.Length > maxLength)
            throw new DomainException("TEMPLATE_INVALID", "Rendered template exceeds the allowed length.", 409);
        return rendered;
    }
}

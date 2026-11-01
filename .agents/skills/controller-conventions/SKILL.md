---
name: controller-conventions
description: Canonical standards for ASP.NET Core thin controllers in NotificationService (port 8007). Covers boundary validation, Notification.Application shared request/response DTOs, English sentence-case error messages, user preference and device token conventions, and the reference controller blueprint.
---

# Controller Conventions & Standards in NotificationService

> Scope: NotificationService repository only. Never reference parent workspace paths, root documentation, or sibling service directories.

This skill defines the invariant structural and boundary rules for all REST API controllers in `Notification.Api`.

Controllers serve strictly as **HTTP Transport Boundary Adapters**. They bind incoming HTTP protocol details, extract authenticated recipient identities, invoke Notification Application use cases, and translate results into standardized HTTP responses. They must never contain business decisions, direct database queries, or manual error formatting.

---

## 1. When to Use

Consult and follow this skill whenever you:
- Create a new controller or add action methods in `Notification.Api/Controllers/Public/`.
- Review controller code changes for architectural consistency in NotificationService.
- Bind request parameters (`[FromRoute]`, `[FromQuery]`, `[FromBody]`).
- Design validation placement for incoming client payloads.
- Format outbound responses, pagination metadata, or status codes.

---

## 2. Request and Response DTO Placement (Application Layer Sharing)

To prevent duplicate wrapper classes and maintain clean domain boundaries:

- **Declare DTOs in Notification.Application**: All Request and Response records are declared directly in `Notification.Application/Services/{Feature}/{Feature}Requests.cs` and `{Feature}Responses.cs`.
- **Direct Consumption by Controller**: Controllers directly bind these Application-layer request records and return these Application-layer response records.
- **Prohibit Duplicate API DTOs**: Never create redundant DTOs in `Notification.Api` (e.g. prohibited: creating `ApiSendEmailRequest` in Api only to map into `SendEmailRequest` in Application).
- **Domain Entities Stay Private**: Never expose EF Core entities or domain aggregate roots (`Notification`, `NotificationPreference`, `DeviceToken`) through the controller. Only immutable DTO records and `PagedData<T>` leave the application boundary.

---

## 3. Validation Placement (Boundary Discipline)

Validation is concentrated exclusively at system boundaries:

- **Boundary Validation on Request Records**: Place declarative validation attributes from `System.ComponentModel.DataAnnotations` directly on Request DTO records (`[Required]`, `[EmailAddress]`, `[StringLength]`, `[RegularExpression]`).
- **Automatic Model State Rejection**: Invalid inputs are intercepted automatically by ASP.NET Core model validation via `ApiValidationProblemFactory`, producing standardized RFC 9110 ProblemDetails with `errorCode = "VAL_INVALID_INPUT"`.
- **PROHIBIT Manual Validation in Controllers**: Never write ad-hoc validation checks inside controller action bodies (e.g. prohibited: `if (string.IsNullOrWhiteSpace(request.ToEmail)) return BadRequest(...);`).

---

## 4. Error Message Standards (Descriptive Sentence Case)

When domain exceptions, validation failures, or notification dispatch guards trigger errors:

- **100% English Language**: All error messages, failure details, and exception descriptions must be written in English.
- **Sentence Case Format**:
  - Capitalize ONLY the first letter of the sentence.
  - All subsequent words must be lowercase, except technical acronyms and identifiers (`ID`, `OTP`, `JWT`, `URL`, `UUID`, `Email`, `API`, `HTTP`).
  - Prohibit ALL-CAPS messages (e.g. prohibited: `"NOTIFICATION NOT FOUND"`, `"INVALID EMAIL"`).
  - Prohibit Title Case (e.g. prohibited: `"Notification Not Found"`).
- **Contextual Precision**: The message must precisely describe what condition failed so any developer or client can diagnose the issue immediately.
  - Correct: `"Notification was not found or does not belong to the user."`
  - Correct: `"Email recipient address is invalid or malformed."`
  - Correct: `"Device token is invalid or has already expired."`
- **Error Codes**: Always `SCREAMING_SNAKE_CASE` without arbitrary `ERR_` prefixes (`NOTIFICATION_NOT_FOUND`, `INVALID_EMAIL`, `DEVICE_TOKEN_INVALID`, `VAL_INVALID_INPUT`, `FORBIDDEN`).

---

## 5. Canonical Reference Controller Blueprint

The following structural skeleton represents the standardized pattern for controllers in `Notification.Api`:

```csharp
namespace Notification.Api.Controllers.Public;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Notification.Api.Extensions;
using Notification.Api.Filters;
using Notification.Application.Common.Models;
using Notification.Application.Services.{Feature};

[ApiController]
[Route("api/v1/{plural-resources}")]
public sealed class {Feature}Controller : ControllerBase
{
    private readonly I{Feature}Service _service;
    private readonly ILogger<{Feature}Controller> _logger;

    public {Feature}Controller(
        I{Feature}Service service,
        ILogger<{Feature}Controller> logger)
    {
        _service = service;
        _logger = logger;
    }

    /// <summary>Lists notifications for the authenticated user with pagination.</summary>
    [HttpGet("me")]
    [RequireRole]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedData<{Feature}Dto>>> ListMy(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (!this.TryGetUserId(out var userId, out var authError))
        {
            return authError;
        }

        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 100) pageSize = 20;

        _logger.LogInformation("[REST {Feature}] Fetching notifications for UserId: '{UserId}'", userId);
        var result = await _service.ListByUserIdAsync(userId, page, pageSize, cancellationToken);
        return Ok(result);
    }

    /// <summary>Dispatches a notification from an Application-layer request record.</summary>
    [HttpPost]
    [RequireRole("admin", "staff")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Send(
        [FromBody] Send{Feature}Request request,
        CancellationToken cancellationToken)
    {
        var result = await _service.SendAsync(request, cancellationToken);
        return Ok(result);
    }
}
```

---

## 6. HTTP Status & Return Conventions

- **GET Single Item**: Return `Task<ActionResult<T>>` with `Ok(dto)` (HTTP 200).
- **GET Paginated List**: Return `Task<ActionResult<PagedData<T>>>` with `Ok(pagedData)` (HTTP 200).
- **POST Creation/Dispatch**: Return `Task<IActionResult>` with `Ok(dto)` or `StatusCode(StatusCodes.Status201Created, dto)`.
- **DELETE or Empty Operations**: Return `Task<IActionResult>` with `NoContent()` (HTTP 204, 0-byte payload).
- **No Manual Outer Envelopes**: Microservice controllers return pure payloads. Wrapping into `ApiResponse<T>` is performed by Gateway `ResponseEnvelopeMiddleware`.
- **Automatic Error Responses**: All unhandled domain exceptions bubble up to `GlobalExceptionHandlerMiddleware` to format RFC 9110 ProblemDetails.

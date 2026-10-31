# 05 — ERROR HANDLING & ENUM BOUNDARY (NOTIFICATION SERVICE)

## 1. 100% English & Sentence Case
* Error messages must be 100% English sentence case.
* Capitalize only the first letter. Keep technical acronyms uppercase (`ID`, `OTP`, `SMTP`, `URL`).
* Examples: `"Notification not found or does not belong to the user."`, `"Device token cannot be empty."`, `"Email address is invalid."`

## 2. SCREAMING_SNAKE_CASE Error Codes
* Error codes must be uppercase with underscores. No `ERR_` prefixes.
* Use `VAL_` prefix for validation errors.
* Examples: `NOTIFICATION_NOT_FOUND`, `INVALID_EMAIL`, `VAL_INVALID_INPUT`, `UNAUTHORIZED`, `FORBIDDEN`.

## 3. RFC 9110 ProblemDetails Structure
* Handled by `GlobalExceptionHandlerMiddleware.cs`: returns `application/problem+json` containing `type`, `title`, `status`, `errorCode`, `detail`, `traceId`.

## 4. Enum Persistence & Boundary Serialization
* In PostgreSQL (`notification_db`): 100% stored as `integer` (int32). Prohibit `HasConversion<string>()`.
* Enums: `NotificationType` (`System = 0`, `OrderPaid = 1`, `AuctionWon = 2`), `NotificationStatus` (`Unread = 0`, `Read = 1`).
* REST Outbound: 100% serialized as `PascalCase` strings (`"System"`, `"OrderPaid"`, `"Unread"`).
* REST Inbound: Handled via `PascalCaseJsonStringEnumConverter(allowIntegerValues: false)`.

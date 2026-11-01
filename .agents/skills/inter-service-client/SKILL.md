---
name: inter-service-client
description: Add or modify outbound typed REST clients in NotificationService (port 8007). Use when NotificationService queries user profile data from UserService or ships centralized audit telemetry to LogService without using message brokers.
---

# Outbound REST Client in NotificationService

> Scope: NotificationService repository only. Never reference parent workspace paths, root documentation, or sibling service directories.

## Local Facts

- Service: NotificationService
- Local HTTP Port: 8007
- Primary Downstream Targets:
  - UserService (`8001`): Resolving user email address or communication preferences (`IUserClient`)
  - LogService (`8004`): Dispatching aggregated delivery failure telemetry (`ILogClient`)
- Interface Location: `Notification.Application/Interfaces/`
- Implementation Location: `Notification.Infrastructure/Services/{Target}/`
- DI Registration: `Notification.Infrastructure/DependencyInjection.cs`

## Invariants

1. **100% RESTful HTTP/JSON:** No RabbitMQ, MassTransit, Saga, or Outbox. All cross-service calls use typed `HttpClient`.
2. **Strict Project Isolation:** Never reference external assemblies or query other microservice databases directly.
3. **No Internal API Key:** Inter-service routes use standard public endpoints. Do not send `X-Internal-Service-Key`.
4. **Timeout & Fallback:** Outbound lookups must enforce a strict timeout ($\le 5$s) and never cause in-app notification creation or background worker queues to crash.

## Implementation Steps

### 1. Application Contract
Define `Notification.Application/Interfaces/IUserClient.cs`:
```csharp
namespace Notification.Application.Interfaces;

public interface IUserClient
{
    Task<string?> GetUserEmailAsync(Guid userId, CancellationToken cancellationToken = default);
}
```

### 2. Infrastructure Typed Client
Implement `Notification.Infrastructure/Services/User/UserClient.cs`:
```csharp
namespace Notification.Infrastructure.Services.User;

public sealed class UserClient : IUserClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<UserClient> _logger;

    public UserClient(HttpClient httpClient, ILogger<UserClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<string?> GetUserEmailAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"api/v1/users/{userId}", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("[REST User Client] User lookup failed with status: {Status}", response.StatusCode);
                return null;
            }

            var user = await response.Content.ReadFromJsonAsync<UserDto>(cancellationToken: cancellationToken);
            return user?.Email;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[REST User Client] Failed to reach UserService for userId '{UserId}'.", userId);
            return null;
        }
    }

    private sealed record UserDto(string Email);
}
```

### 3. Dependency Injection Registration
In `Notification.Infrastructure/DependencyInjection.cs`:
```csharp
var userBaseUrl = Environment.GetEnvironmentVariable("USER_SERVICE_URL")
    ?? configuration["USER_SERVICE_URL"]
    ?? configuration["Services:User:BaseUrl"]
    ?? "http://localhost:8001/";

services.AddHttpClient<IUserClient, UserClient>(client =>
{
    client.BaseAddress = new Uri(userBaseUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(5);
});
```

### 4. Configuration Synchronization
Declare `USER_SERVICE_URL` in `docker-compose.yml` and `.env.example`.

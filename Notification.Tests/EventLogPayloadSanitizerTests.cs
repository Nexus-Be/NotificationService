
using Notification.Api.Controllers;
using Xunit;

namespace Notification.Tests;

public sealed class EventLogPayloadSanitizerTests
{
    // TEST 1: Only approved fields are returned
    [Fact]
    public void Sanitize_OrderPaid_ReturnsOnlyAllowedFields()
    {
        var orderId = Guid.NewGuid();

        var json = $$"""
        {
            "orderId": "{{orderId}}",
            "totalAmount": 250000,
            "email": "user@gmail.com",
            "accessToken": "secret-token",
            "password": "secret-password"
        }
        """;

        var result = EventLogPayloadSanitizer.Sanitize(
            "OrderPaid",
            json);

        Assert.NotNull(result);

        Assert.Equal(2, result.Count);

        Assert.Equal(
            orderId.ToString(),
            result["orderId"]?.GetValue<string>());

        Assert.Equal(
            250000m,
            result["totalAmount"]?.GetValue<decimal>());

        Assert.False(result.ContainsKey("email"));
        Assert.False(result.ContainsKey("accessToken"));
        Assert.False(result.ContainsKey("password"));
    }

    // TEST 2: Unknown events must not expose payload
    [Fact]
    public void Sanitize_UnknownEvent_ReturnsNull()
    {
        var json = """
        {
            "email": "user@gmail.com",
            "password": "secret"
        }
        """;

        var result = EventLogPayloadSanitizer.Sanitize(
            "UnknownEvent",
            json);

        Assert.Null(result);
    }

    // TEST 3: Malformed JSON must be rejected
    [Fact]
    public void Sanitize_InvalidJson_ReturnsNull()
    {
        var invalidJson = "{ invalid json }";

        var result = EventLogPayloadSanitizer.Sanitize(
            "OrderPaid",
            invalidJson);

        Assert.Null(result);
    }

    // TEST 4: Invalid business data is rejected
    [Fact]
    public void Sanitize_InvalidOrderData_ReturnsNull()
    {
        var json = """
        {
            "orderId": "invalid-guid",
            "totalAmount": -100,
            "email": "user@gmail.com"
        }
        """;

        var result = EventLogPayloadSanitizer.Sanitize(
            "OrderPaid",
            json);

        Assert.Null(result);
    }

    // TEST 5: Empty payload returns null
    [Fact]
    public void Sanitize_EmptyPayload_ReturnsNull()
    {
        var result = EventLogPayloadSanitizer.Sanitize(
            "OrderPaid",
            "");

        Assert.Null(result);
    }
}

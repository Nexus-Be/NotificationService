namespace Notification.Tests;

using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Notification.Api.Common;
using Xunit;

public sealed class ApiValidationProblemFactoryTests
{
    [Fact]
    public void Create_Returns_Problem_With_Validation_Error_Code()
    {
        var actionContext = CreateActionContext();
        actionContext.ModelState.AddModelError("Amount", "The Amount field is required.");

        var result = ApiValidationProblemFactory.Create(actionContext);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, objectResult.StatusCode);
        Assert.Contains("application/problem+json", objectResult.ContentTypes);

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(objectResult.Value));
        var root = document.RootElement;
        Assert.Equal("VAL_INVALID_INPUT", root.GetProperty("errorCode").GetString());
        Assert.Equal(StatusCodes.Status400BadRequest, root.GetProperty("status").GetInt32());
        Assert.Contains("The Amount field is required.", root.GetProperty("detail").GetString());
    }

    [Fact]
    public void Create_Hides_Internal_Type_Names_From_Json_Binding_Errors()
    {
        var actionContext = CreateActionContext();
        actionContext.ModelState.AddModelError(
            "$",
            "The JSON value could not be converted to Notification.Tests.Internal.Request. Path: $ | LineNumber: 0 | BytePositionInLine: 1.");
        actionContext.ModelState.AddModelError("request", "The request field is required.");

        var result = ApiValidationProblemFactory.Create(actionContext);

        var objectResult = Assert.IsType<ObjectResult>(result);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(objectResult.Value));
        var detail = document.RootElement.GetProperty("detail").GetString();
        Assert.Equal(ApiValidationProblemFactory.UnreadableBodyDetail, detail);
        Assert.DoesNotContain("Notification.Tests", detail);
        Assert.DoesNotContain("LineNumber", detail);
    }

    private static ActionContext CreateActionContext() =>
        new(new DefaultHttpContext(), new RouteData(), new ActionDescriptor());
}

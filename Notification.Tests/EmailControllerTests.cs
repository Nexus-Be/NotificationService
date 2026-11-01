namespace Notification.Tests;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Notification.Api.Controllers.Public;
using Notification.Application.Services.Email;
using Xunit;

public sealed class EmailControllerTests
{
    private readonly Mock<IEmailNotificationService> _mockNotificationService;
    private readonly EmailController _controller;

    public EmailControllerTests()
    {
        _mockNotificationService = new Mock<IEmailNotificationService>();
        _controller = new EmailController(
            _mockNotificationService.Object,
            NullLogger<EmailController>.Instance);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    [InlineData("   ")]
    public async Task SendOtpEmail_ReturnsBadRequest_WhenEmailIsInvalid(string invalidEmail)
    {
        var request = new SendOtpEmailApiRequest
        {
            Email = invalidEmail,
            OtpCode = "123456"
        };

        var response = await _controller.SendOtpEmail(request, CancellationToken.None);

        var badRequestResult = Assert.IsType<BadRequestObjectResult>(response);
        Assert.Equal(StatusCodes.Status400BadRequest, badRequestResult.StatusCode);
    }

    [Fact]
    public async Task SendOtpEmail_ReturnsBadRequest_WhenOtpIsEmpty()
    {
        var request = new SendOtpEmailApiRequest
        {
            Email = "valid@example.com",
            OtpCode = ""
        };

        var response = await _controller.SendOtpEmail(request, CancellationToken.None);

        var badRequestResult = Assert.IsType<BadRequestObjectResult>(response);
        Assert.Equal(StatusCodes.Status400BadRequest, badRequestResult.StatusCode);
    }

    [Fact]
    public async Task SendOtpEmail_ReturnsOk_WhenRequestIsValid()
    {
        var request = new SendOtpEmailApiRequest
        {
            Email = "buyer@nexus.local",
            FullName = "Nguyen Van A",
            OtpCode = "998877",
            ExpiresInMinutes = 5
        };

        _mockNotificationService
            .Setup(s => s.SendOtpEmailAsync(It.Is<SendOtpEmailDto>(d =>
                d.Email == "buyer@nexus.local" &&
                d.FullName == "Nguyen Van A" &&
                d.OtpCode == "998877" &&
                d.ExpiresInMinutes == 5), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var response = await _controller.SendOtpEmail(request, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(response);
        Assert.Equal(StatusCodes.Status200OK, okResult.StatusCode);
    }

    [Fact]
    public async Task SendOtpEmail_Returns500_WhenServiceFails()
    {
        var request = new SendOtpEmailApiRequest
        {
            Email = "buyer@nexus.local",
            OtpCode = "998877"
        };

        _mockNotificationService
            .Setup(s => s.SendOtpEmailAsync(It.IsAny<SendOtpEmailDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var response = await _controller.SendOtpEmail(request, CancellationToken.None);

        var statusResult = Assert.IsType<ObjectResult>(response);
        Assert.Equal(StatusCodes.Status500InternalServerError, statusResult.StatusCode);
    }

    [Fact]
    public async Task SendGeneralEmail_ReturnsBadRequest_WhenSubjectOrBodyMissing()
    {
        var request = new SendGeneralEmailApiRequest
        {
            ToEmail = "buyer@nexus.local",
            Subject = "",
            HtmlBody = ""
        };

        var response = await _controller.SendGeneralEmail(request, CancellationToken.None);

        var badRequestResult = Assert.IsType<BadRequestObjectResult>(response);
        Assert.Equal(StatusCodes.Status400BadRequest, badRequestResult.StatusCode);
    }

    [Fact]
    public async Task SendGeneralEmail_ReturnsOk_WhenRequestIsValid()
    {
        var request = new SendGeneralEmailApiRequest
        {
            ToEmail = "buyer@nexus.local",
            Subject = "Test Subject",
            HtmlBody = "<h1>Test Body</h1>"
        };

        _mockNotificationService
            .Setup(s => s.SendEmailAsync(It.IsAny<SendGeneralEmailDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var response = await _controller.SendGeneralEmail(request, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(response);
        Assert.Equal(StatusCodes.Status200OK, okResult.StatusCode);
    }
}

namespace Notification.Tests;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Notification.Api.Controllers.Internal;
using Notification.Application.Services.Email;
using Xunit;

public sealed class InternalEmailControllerTests
{
    private readonly Mock<IEmailNotificationService> _mockNotificationService;
    private readonly InternalEmailController _controller;

    public InternalEmailControllerTests()
    {
        _mockNotificationService = new Mock<IEmailNotificationService>();
        _controller = new InternalEmailController(
            _mockNotificationService.Object,
            NullLogger<InternalEmailController>.Instance);
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid-email")]
    [InlineData("   ")]
    public async Task SendOtpEmail_ReturnsBadRequest_WhenEmailIsInvalid(string invalidEmail)
    {
        var request = new InternalSendOtpEmailApiRequest
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
        var request = new InternalSendOtpEmailApiRequest
        {
            Email = "valid@example.com",
            OtpCode = ""
        };

        var response = await _controller.SendOtpEmail(request, CancellationToken.None);

        var badRequestResult = Assert.IsType<BadRequestObjectResult>(response);
        Assert.Equal(StatusCodes.Status400BadRequest, badRequestResult.StatusCode);
    }

    [Fact]
    public async Task SendOtpEmail_WithCustomPurpose_PassesPurposeToDto()
    {
        var request = new InternalSendOtpEmailApiRequest
        {
            Email = "user@nexus.local",
            FullName = "Alex Rider",
            OtpCode = "889900",
            ExpiresInMinutes = 10,
            Purpose = "password_reset"
        };

        _mockNotificationService
            .Setup(s => s.SendOtpEmailAsync(
                It.Is<SendOtpEmailDto>(d =>
                    d.Email == "user@nexus.local" &&
                    d.FullName == "Alex Rider" &&
                    d.OtpCode == "889900" &&
                    d.ExpiresInMinutes == 10 &&
                    d.Purpose == "password_reset"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var response = await _controller.SendOtpEmail(request, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(response);
        Assert.Equal(StatusCodes.Status200OK, okResult.StatusCode);
        _mockNotificationService.Verify(s => s.SendOtpEmailAsync(
            It.Is<SendOtpEmailDto>(d => d.Purpose == "password_reset"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendOtpEmail_WithoutPurpose_DefaultsToRegistration()
    {
        var request = new InternalSendOtpEmailApiRequest
        {
            Email = "user@nexus.local",
            FullName = "Alex Rider",
            OtpCode = "889900",
            ExpiresInMinutes = 5,
            Purpose = null
        };

        _mockNotificationService
            .Setup(s => s.SendOtpEmailAsync(
                It.Is<SendOtpEmailDto>(d =>
                    d.Email == "user@nexus.local" &&
                    d.FullName == "Alex Rider" &&
                    d.OtpCode == "889900" &&
                    d.ExpiresInMinutes == 5 &&
                    d.Purpose == "registration"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var response = await _controller.SendOtpEmail(request, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(response);
        Assert.Equal(StatusCodes.Status200OK, okResult.StatusCode);
    }

    [Fact]
    public async Task SendOtpEmail_Returns500_WhenServiceFails()
    {
        var request = new InternalSendOtpEmailApiRequest
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
    public async Task SendGeneralEmail_ReturnsOk_WhenRequestIsValid()
    {
        var request = new InternalSendGeneralEmailApiRequest
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

    [Fact]
    public async Task SendGeneralEmail_ReturnsBadRequest_WhenSubjectOrBodyMissing()
    {
        var request = new InternalSendGeneralEmailApiRequest
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
    public async Task SendSellerRejectionEmail_WithValidRequest_ReturnsOkResult()
    {
        var request = new InternalSendSellerRejectionEmailApiRequest
        {
            Email = "seller@example.com",
            FullName = "Seller Name",
            ShopName = "My Shop",
            RejectReason = "Incomplete business documents."
        };

        _mockNotificationService
            .Setup(s => s.SendEmailAsync(It.IsAny<SendGeneralEmailDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _controller.SendSellerRejectionEmail(request, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
        Assert.Equal(StatusCodes.Status200OK, okResult.StatusCode);
    }

    [Fact]
    public async Task SendSellerRejectionEmail_WithEmptyReason_ReturnsBadRequest()
    {
        var request = new InternalSendSellerRejectionEmailApiRequest
        {
            Email = "seller@example.com",
            RejectReason = ""
        };

        var result = await _controller.SendSellerRejectionEmail(request, CancellationToken.None);

        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, badRequestResult.StatusCode);
    }

    [Fact]
    public async Task SendSellerRejectionEmail_WhenServiceFails_Returns500InternalServerError()
    {
        var request = new InternalSendSellerRejectionEmailApiRequest
        {
            Email = "seller@example.com",
            FullName = "Seller Name",
            ShopName = "My Shop",
            RejectReason = "Incomplete business documents."
        };

        _mockNotificationService
            .Setup(s => s.SendEmailAsync(It.IsAny<SendGeneralEmailDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await _controller.SendSellerRejectionEmail(request, CancellationToken.None);

        var statusResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, statusResult.StatusCode);
    }
}

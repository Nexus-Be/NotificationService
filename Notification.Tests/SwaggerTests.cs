namespace Notification.Tests;

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using Notification.Api.Extensions;
using Swashbuckle.AspNetCore.Swagger;
using Xunit;

public sealed class SwaggerTests
{
    [Fact]
    public void AddServiceDefaults_RegistersSwaggerProvider_AndGeneratesValidOpenApiDocument()
    {
        // Arrange
        const string serviceName = "Notification Service";
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddControllers();

        var mockEnvironment = new Mock<IWebHostEnvironment>();
        mockEnvironment.Setup(m => m.ApplicationName).Returns("Notification.Api");
        mockEnvironment.Setup(m => m.EnvironmentName).Returns(Environments.Development);
        services.AddSingleton(mockEnvironment.Object);
        services.AddSingleton<IHostEnvironment>(mockEnvironment.Object);

        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();

        services.AddServiceDefaults(configuration, serviceName);
        var serviceProvider = services.BuildServiceProvider();

        // Act
        var swaggerProvider = serviceProvider.GetService<ISwaggerProvider>();

        // Assert
        Assert.NotNull(swaggerProvider);
        var openApiDoc = swaggerProvider.GetSwagger("v1");
        Assert.NotNull(openApiDoc);
        Assert.Equal($"Nexus {serviceName} REST API", openApiDoc.Info.Title);
        Assert.Equal("v1", openApiDoc.Info.Version);
        Assert.True(openApiDoc.Components.SecuritySchemes.ContainsKey("Bearer"));
        Assert.True(openApiDoc.Components.SecuritySchemes.ContainsKey("x-user-id"));
    }
}

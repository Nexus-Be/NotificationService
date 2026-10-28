namespace Notification.Application.Consumers;

using MassTransit;
using Notification.Application.Consumers.User;

public static class ConsumerExtensions
{
    public static IBusRegistrationConfigurator AddApplicationConsumers(this IBusRegistrationConfigurator configurator)
    {
        configurator.AddConsumer<SendOtpEmailConsumer>()
            .Endpoint(e => e.Name = "send-otp-email");

        return configurator;
    }
}

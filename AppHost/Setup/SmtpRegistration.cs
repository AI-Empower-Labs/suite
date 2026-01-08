using AppHost.Extensions;
using AppHost.Services;

namespace AppHost.Setup;

internal static class SmtpRegistration
{
	[ResourceRegistrationOrder(100)]
	public static IResourceBuilder<MailPitContainerResource> Register(IDistributedApplicationBuilder builder)
	{
		int port = PortAllocationHelper.GetNextAvailablePort();
		return builder
			.AddMailPit(ResourceNames.Mailpit, httpPort: port, smtpPort: port + 1)
			.WithDefaults()
			.WithUrlForEndpoint("http", static url => url.DisplayText = "📩 SMTP Dashboard")
			.WithUrlForEndpoint("smtp", static url => url.DisplayLocation = UrlDisplayLocation.DetailsOnly)
			.WithIconName("Mail");
	}
}

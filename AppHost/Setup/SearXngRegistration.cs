using System.Reflection;

using AppHost.Extensions;
using AppHost.Services;

namespace AppHost.Setup;

internal static class SearXngRegistration
{
	[ResourceRegistrationOrder(100)]
	public static IResourceBuilder<ContainerResource> Register(IDistributedApplicationBuilder builder)
	{
		IResourceBuilder<ParameterResource> searXngSecret = builder
			.AddParameter(
				"sear-xng-secret",
				"ULo9a5fNaSsHYbf4Aq8gylnauIBypYcY",
				secret: true);
		BinaryData binaryData = BinaryData.FromStream(
			Assembly.GetExecutingAssembly().GetManifestResourceStream("AppHost.Resources.searxng_settings.yaml")!);
		File.WriteAllText("searxng_settings.yaml", binaryData.ToString());

		return builder
			.AddContainerEx(ResourceNames.SearXng, "SEARXNG_IMAGE", "searxng/searxng", "latest", "docker.io")
			.WithDefaults()
			.WithHttpEndpoint(port: PortAllocationHelper.GetNextAvailablePort(), targetPort: 8080)
			.WithUrlForEndpoint("http", static url => { url.DisplayText = "SearXng"; })
			.WithHttpHealthCheck("/healthz", 200, "http")
			.WithEnvironment("SEARXNG_SECRET", searXngSecret)
			.WithBindMount("./searxng_settings.yaml", "/etc/searxng/settings.yml", true)
			.WithCurlHttpHealthCheckEndpoint("http://localhost:8080/healthz");
	}
}

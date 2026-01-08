using AppHost.Extensions;
using AppHost.Services;

namespace AppHost.Setup;

internal static class QdrantRegistration
{
	[ResourceRegistrationOrder(100)]
	public static IResourceBuilder<QdrantServerResource> Register(IDistributedApplicationBuilder builder)
	{
		int port = PortAllocationHelper.GetNextAvailablePort();
		return builder
			.AddQdrant(ResourceNames.Qdrant, grpcPort: port, httpPort: port + 1)
			.WithImageEx("QDRANT_IMAGE", "ai-empower-labs/qdrant", "1.0.0")
			.WithDefaults()
			.WithIconName("fluent:database-16-regular")
			.WithUrlForEndpoint("grpc", static url => { url.DisplayLocation = UrlDisplayLocation.DetailsOnly; })
			.WithDataVolume()
			.WithCurlHttpHealthCheckEndpoint("http://localhost:6333/healthz");
	}

	public static void Register(
		[Services.ResourceName(ResourceNames.Qdrant)] IResourceBuilder<QdrantServerResource> qdrant,
		[Services.ResourceName(ResourceNames.OpenTelemetryName)] IResourceBuilder<OpenTelemetryCollectorResource> otelCollector)
	{
		otelCollector.WithEnvironment("QDRANT_ENDPOINT", qdrant.GetEndpoint("http").Property(EndpointProperty.HostAndPort));
		otelCollector.WithEnvironment("QDRANT_API_KEY", qdrant.Resource.ApiKeyParameter);
	}
}

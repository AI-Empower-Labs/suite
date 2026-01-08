using AppHost.Extensions;
using AppHost.Services;

using Aspire.Hosting.Docker.Resources.ServiceNodes;

namespace AppHost.Setup;

internal static class RedisRegistration
{
	[ResourceRegistrationOrder(100)]
	public static IResourceBuilder<RedisResource> Register(IDistributedApplicationBuilder builder)
	{
		int port = PortAllocationHelper.GetNextAvailablePort();
		IResourceBuilder<RedisResource> resourceBuilder = builder
			.AddRedis(ResourceNames.Redis, port: port)
			.WithRedisCommander(x => x.WithUrlForEndpoint("http", url => url.DisplayText = "Redis Commander"))
			.WithDefaults()
			.WithIconName("fluent:database-16-regular")
			.WithUrlForEndpoint("tcp", static url => { url.DisplayLocation = UrlDisplayLocation.DetailsOnly; })
			.WithDataVolume()
			.WithPersistence()
			.PublishAsDockerComposeService((_, service) =>
			{
				service.Healthcheck = new Healthcheck
				{
					Test = ["CMD", "redis-cli", "ping"],
					Interval = "10s",
					Timeout = "5s",
					Retries = 5,
					StartPeriod = "30s"
				};
			})
			.WithoutHttpsCertificate();
		return resourceBuilder;
	}

	public static void Register(
		[Services.ResourceName(ResourceNames.Redis)] IResourceBuilder<RedisResource> redis,
		[Services.ResourceName(ResourceNames.OpenTelemetryName)] IResourceBuilder<OpenTelemetryCollectorResource> otelCollector)
	{
		otelCollector.WithEnvironment("REDIS_ENDPOINT", redis.Resource.PrimaryEndpoint.Property(EndpointProperty.HostAndPort));
		otelCollector.WithEnvironment("REDIS_PASSWORD", redis.Resource.PasswordParameter!);
	}
}

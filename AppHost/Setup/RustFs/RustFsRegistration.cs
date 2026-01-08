using AppHost.Extensions;
using AppHost.Services;

using Aspire.Hosting.Docker.Resources.ServiceNodes;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AppHost.Setup.RustFs;

internal static class RustFsRegistration
{
	[ResourceRegistrationOrder(100)]
	public static IResourceBuilder<RustFsContainerResource> Register(IDistributedApplicationBuilder builder)
	{
		IResourceBuilder<ParameterResource> rustFsAccessKey = builder
			.AddParameter(ResourceNames.RustFsAccessKey, "rustadmin", true);
		IResourceBuilder<ParameterResource> rustFsSecretKey = builder
			.AddParameter(ResourceNames.RustFsSecretKey, true);

		int port = PortAllocationHelper.GetNextAvailablePort();
		RustFsContainerResource rustFsContainerResource = new(ResourceNames.RustFs)
		{
			AccessKey = rustFsAccessKey.Resource,
			SecretKey = rustFsSecretKey.Resource,
			Region = RustFsInitialization.RustFsRegion(builder.Configuration)
		};
		IResourceBuilder<RustFsContainerResource> rustfs = builder
			.AddResource(rustFsContainerResource)
			.WithImageEx("RUSTFS_IMAGE", "rustfs/rustfs", "latest", "docker.io")
			.WithDefaults()
			.WithHttpEndpoint(port: port, targetPort: 9000, name: RustFsContainerResource.PrimaryEndpointName)
			.WithHttpEndpoint(port: port + 1, targetPort: 9001, name: "console")
			.WithUrlForEndpoint("api", annotation => { annotation.DisplayLocation = UrlDisplayLocation.DetailsOnly; })
			.WithUrlForEndpoint("console", annotation => { annotation.DisplayText = "Console"; })
			.WithEnvironment("RUSTFS_ACCESS_KEY", rustFsContainerResource.AccessKey)
			.WithEnvironment("RUSTFS_SECRET_KEY", rustFsContainerResource.SecretKey)
			.WithEnvironment("RUSTFS_ADDRESS", "0.0.0.0:9000")
			.WithEnvironment("RUSTFS_CONSOLE_ADDRESS", "0.0.0.0:9001")
			.WithEnvironment("RUSTFS_CONSOLE_ENABLE", "true")
			.OnResourceReady(async (resource, readyEvent, cancellationToken) =>
			{
				string? scheme = await resource.PrimaryEndpoint.Property(EndpointProperty.Scheme).GetValueAsync(cancellationToken);
				string? host = await resource.PrimaryEndpoint.Property(EndpointProperty.HostAndPort).GetValueAsync(cancellationToken);
				await RustFsInitialization.Initialize(
					readyEvent.Services.GetRequiredService<ILoggerFactory>().CreateLogger("RustFsInitialization"),
					rustFsContainerResource,
					$"{scheme}://{host}",
					["flowise", ResourceNames.RustFsPostgresBackupBucketName, ResourceNames.LangfuseBucketName],
					cancellationToken);
			})
			.PublishAsDockerComposeService((_, service) =>
			{
				service.Healthcheck = new Healthcheck
				{
					Test = ["CMD", "sh", "-c", "curl -f http://localhost:9000/health && curl -f curl -f http://localhost:9001/health"],
					Interval = "10s",
					Timeout = "5s",
					Retries = 5,
					StartPeriod = "30s"
				};
			});

		return rustfs;
	}

	[ResourceRegistrationOrder(1000)]
	public static void Register(
		[Services.ResourceName(ResourceNames.RustFs)] IResourceBuilder<RustFsContainerResource> rustfs,
		[Services.ResourceName(ResourceNames.OpenTelemetryName)] IResourceBuilder<OpenTelemetryCollectorResource> openTelemetryCollector)
	{
		rustfs
			.WithEnvironment("RUSTFS_OBS_LOGGER_LEVEL", "info")
			.WithEnvironment("RUSTFS_OBS_ENDPOINT", openTelemetryCollector.Resource.GetEndpoint("http"));
	}
}

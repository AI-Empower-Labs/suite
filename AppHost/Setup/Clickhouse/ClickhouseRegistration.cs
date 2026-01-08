using AppHost.Extensions;
using AppHost.Services;

using Aspire.Hosting.Docker.Resources.ServiceNodes;

namespace AppHost.Setup.Clickhouse;

internal static class ClickhouseRegistration
{
	[ResourceRegistrationOrder(100)]
	public static IResourceBuilder<ClickhouseResource> Register(IDistributedApplicationBuilder builder)
	{
		int port = PortAllocationHelper.GetNextAvailablePort();
		ClickhouseResource clickhouseResource = new(ResourceNames.Clickhouse)
		{
			Password = ParameterResourceBuilderExtensions
				.CreateDefaultPasswordParameter(builder, ResourceNames.ClickhousePassword, special: false, upper: false, numeric: false),
			Database = "default",
			UserName = "clickhouse"
		};
		IResourceBuilder<ClickhouseResource> clickhouse = builder
			.AddResource(clickhouseResource)
			.WithImageEx("CLICKHOUSE_IMAGE", "clickhouse/clickhouse-server", "25.11", "docker.io")
			.WithContainerRuntimeArgs("--ulimit", "nofile=262144:262144")
			.WithContainerRuntimeArgs("--cap-add", "SYS_NICE")
			.WithContainerRuntimeArgs("--cap-add", "NET_ADMIN")
			.WithContainerRuntimeArgs("--cap-add", "IPC_LOCK")
			.WithDefaults()
			.WithHttpEndpoint(
				port: port,
				targetPort: 8123).WithUrlForEndpoint("http",
				static url =>
				{
					url.DisplayText = "Dashboard";
					url.Url = "/play";
				})
			.WithHttpEndpoint(
				port: port + 1,
				targetPort: 9000,
				name: "migration")
			.WithUrlForEndpoint("http", static url => { url.DisplayLocation = UrlDisplayLocation.DetailsOnly; })
			.WithCurlHttpHealthCheckEndpoint("http://localhost:8123/?query=SELECT%201")
			.WithEnvironment("CLICKHOUSE_DB", clickhouseResource.Database)
			.WithEnvironment("CLICKHOUSE_USER", clickhouseResource.UserName)
			.WithEnvironment("CLICKHOUSE_PASSWORD", clickhouseResource.Password)
			.PublishAsDockerComposeService((_, service) =>
			{
				service.Ulimits.Add("nofile",
					new Ulimit
					{
						Soft = 262144,
						Hard = 262144
					});
				service.CapAdd.Add("SYS_NICE");
				service.CapAdd.Add("NET_ADMIN");
				service.CapAdd.Add("IPC_LOCK");
			});
		clickhouse
			.WithVolume(VolumeNameGenerator.Generate(clickhouse, "data"), "/var/lib/clickhouse/")
			.WithVolume(VolumeNameGenerator.Generate(clickhouse, "logs"), "/var/log/clickhouse-server/");

		return clickhouse;
	}
}

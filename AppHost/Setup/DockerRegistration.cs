using AppHost.Services;

using Aspire.Hosting.Docker;
using Aspire.Hosting.Docker.Resources.ComposeNodes;
using Aspire.Hosting.Docker.Resources.ServiceNodes;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AppHost.Setup;

internal static class DockerRegistration
{
	[ResourceRegistrationOrder(100)]
	public static IResourceBuilder<DockerComposeEnvironmentResource> Register(IDistributedApplicationBuilder builder)
	{
		string deploymentName = builder.Configuration.GetValue<string>("DEPLOYMENT_NAME") ?? "ael";

		builder.Eventing.Subscribe<BeforeStartEvent>((e, _) =>
		{
			DistributedApplicationModel appModel = e.Services.GetRequiredService<DistributedApplicationModel>();
			foreach (IComputeResource computeResource in appModel.Resources.OfType<IComputeResource>())
			{
				IResourceBuilder<IComputeResource> resourceBuilder = builder.CreateResourceBuilder(computeResource);
				PublishAsDockerComposeServiceWithDefaults(resourceBuilder);
			}

			return Task.CompletedTask;
		});

		return builder
			.AddDockerComposeEnvironment(deploymentName)
			.ConfigureEnvFile(env =>
			{
				env.Remove("HTTP://LOCALHOST:18889");
			})
			.WithProperties(resource => resource.DefaultNetworkName = deploymentName)
			.WithDashboard(false)
			.ConfigureComposeFile(file =>
			{
				file.Name = "AI Empower Labs";
				string traefikNetwork = builder.Configuration.GetValue<string>("TRAEFIK_NETWORK") ?? "traefik";
				file.AddNetwork(new Network
				{
					Name = traefikNetwork,
					External = true,
					Labels = new Dictionary<string, string>
					{
						["com.docker.compose.network.external.name"] = traefikNetwork
					}
				});
			});
	}

	private static void PublishAsDockerComposeServiceWithDefaults<T>(IResourceBuilder<T> builder,
		Action<DockerComposeServiceResource, Service>? configure = null)
		where T : IComputeResource
	{
		ArgumentNullException.ThrowIfNull(builder);
		if (!builder.ApplicationBuilder.IsProductionEnvironment)
		{
			return;
		}

		builder
			.PublishAsDockerComposeService((resource, service) =>
			{
				service.Restart = "unless-stopped";
				service.ExtraHosts.Add("host.docker.internal", "host-gateway");

				// Configure Docker logging for this service
				service.Logging = new Logging
				{
					Driver = "json-file",
					Options = new Dictionary<string, string>
					{
						["max-size"] = "10m",
						["max-file"] = "3"
					}
				};

				foreach (KeyValuePair<string, ServiceDependency> serviceDependency in service.DependsOn)
				{
					serviceDependency.Value.Condition = "service_healthy";
				}

				service.DomainName = builder.Resource.Name;

				service.Labels.Add("company.name", "AI Empower Labs");
				service.Labels.Add("company.contact.email", "support@aiempowerlabs.com");
				service.Labels.Add("com.docker.compose.project", "AI Empower Labs Suite");

				if (builder.Resource.TryGetAnnotationsOfType<EndpointAnnotation>(out IEnumerable<EndpointAnnotation>? endpoints))
				{
					foreach (EndpointAnnotation endpointAnnotation in endpoints.Where(endpoint => endpoint.IsExternal))
					{
						string instanceName = $"{resource.Parent.Name}-{builder.Resource.Name}";
						service.Labels.Add("traefik.enable", "true");
						service.Labels.Add($"traefik.http.routers.{instanceName}.rule", $"Host(`{service.DomainName}`)");
						service.Labels.Add($"traefik.http.routers.{instanceName}.entrypoints", "websecure");
						service.Labels.Add($"traefik.http.services.{instanceName}.loadbalancer.server.port", endpointAnnotation.TargetPort?.ToString() ?? throw new Exception("Missing target port"));

						string traefikNetwork = builder.ApplicationBuilder.Configuration.GetValue<string>("TRAEFIK_NETWORK") ?? "traefik";
						service.Networks.Add(traefikNetwork);
						break;
					}
				}

				foreach (Volume volume in service.Volumes)
				{
					if (string.IsNullOrEmpty(volume.Source)
						|| !volume.Source.StartsWith('/')
						|| !File.Exists(volume.Source))
					{
						continue;
					}

					if (!volume.Type?.Equals("bind", StringComparison.OrdinalIgnoreCase) ?? false)
					{
						continue;
					}

					FileInfo fileInfo = new(volume.Source);
					volume.Source = fileInfo.Name;
					DockerComposeEnvironmentResource dockerComposeEnvironmentResource = builder.ApplicationBuilder.Resources.OfType<DockerComposeEnvironmentResource>().Single();
					IResourceBuilder<DockerComposeEnvironmentResource> composeResourceBuilder = builder.ApplicationBuilder.CreateResourceBuilder(dockerComposeEnvironmentResource);
					composeResourceBuilder.ConfigureComposeFile(file =>
					{
						file.AddConfig(new Config
						{
							Name = fileInfo.Name,
							Content = File.ReadAllText(fileInfo.FullName),
							Labels = new Dictionary<string, string>
							{
								["com.docker.compose.service"] = service.Name
							}
						});
					});
				}

				configure?.Invoke(resource, service);
			});
	}
}

using Aspire.Hosting.Docker.Resources.ServiceNodes;

using Microsoft.Extensions.Configuration;

namespace AppHost.Extensions;

internal static class ResourceBuilderExtensions
{
	extension<T>(IResourceBuilder<T> builder) where T : IResource
	{
		public IResourceBuilder<T> HideResource()
		{
			ArgumentNullException.ThrowIfNull(builder);
			return builder
				.WithInitialState(new CustomResourceSnapshot
				{
					ResourceType = typeof(T).Name,
					Properties = [],
					IsHidden = true,
				});
		}
	}

	extension<T>(IResourceBuilder<T> builder) where T : ContainerResource
	{
		public IResourceBuilder<T> WithDefaults()
		{
			ArgumentNullException.ThrowIfNull(builder);
			return builder
				.WithLifetime(builder.ApplicationBuilder.ContainerLifetime)
				.WithContainerRuntimeArgs("--add-host", "host.docker.internal:host-gateway");
		}

		public IResourceBuilder<T> WithImageEx(
			string imageEnvironmentName,
			string imageName,
			string imageTag,
			string imageRegistry = "ghcr.io")
		{
			string? imageNameFromEnvironment = builder.ApplicationBuilder.Configuration.GetValue<string>(imageEnvironmentName);
			if (!string.IsNullOrEmpty(imageNameFromEnvironment))
			{
				return builder.WithImage(imageNameFromEnvironment);
			}

			return builder
				.WithImage(imageName, imageTag)
				.WithImageRegistry(imageRegistry)
				.WithContainerRuntimeArgs("--pull", "always");
		}
	}

	extension<T>(IResourceBuilder<T> builder) where T : IComputeResource
	{
		public IResourceBuilder<T> WithCurlHttpHealthCheckEndpoint(string healthCheckEndpoint)
		{
			return builder
				.PublishAsDockerComposeService((_, service) =>
				{
					service.Healthcheck = new Healthcheck
					{
						Test = ["CMD", "curl", "-f", healthCheckEndpoint],
						Interval = "10s",
						Timeout = "5s",
						Retries = 5,
						StartPeriod = "30s"
					};
				});
		}

		public IResourceBuilder<T> WithWgetHttpHealthCheckEndpoint(string healthCheckEndpoint)
		{
			return builder
				.PublishAsDockerComposeService((_, service) =>
				{
					service.Healthcheck = new Healthcheck
					{
						Test = ["CMD-SHELL", $"wget --no-verbose --tries=1 --spider {healthCheckEndpoint} || exit 1"],
						Interval = "10s",
						Timeout = "5s",
						Retries = 5,
						StartPeriod = "30s"
					};
				});
		}
	}
}

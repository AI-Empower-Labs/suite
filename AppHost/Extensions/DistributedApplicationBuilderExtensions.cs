using AppHost.Extensions;

using Microsoft.Extensions.Configuration;

// ReSharper disable once CheckNamespace
namespace Aspire.Hosting;

internal static class DistributedApplicationBuilderExtensions
{
	extension(IDistributedApplicationBuilder builder)
	{
		public bool IsProductionEnvironment => builder.ExecutionContext.IsPublishMode;

		public bool StartN8N => builder.Configuration["START_N8N"]?.Equals("true", StringComparison.OrdinalIgnoreCase) ?? false;

		public ContainerLifetime ContainerLifetime
		{
			get
			{
				if (!builder.ExecutionContext.IsRunMode)
				{
					return ContainerLifetime.Session;
				}

				string? value = builder.Configuration.GetValue<string>("ContainerLifetime");
				if (string.IsNullOrEmpty(value))
				{
					return ContainerLifetime.Session;
				}

				return value.Contains("Persistent", StringComparison.OrdinalIgnoreCase)
					? ContainerLifetime.Persistent
					: ContainerLifetime.Session;
			}
		}

		public IResourceBuilder<ContainerResource> AddContainerEx(string containerName,
			string imageEnvironmentName,
			string imageName,
			string imageTag,
			string imageRegistry = "ghcr.io")
		{
			return builder
				.AddContainer(containerName, imageName, imageTag)
				.WithImageEx(imageEnvironmentName, imageName, imageTag, imageRegistry);
		}

		public void OnResourceReady(Func<IResource[], CancellationToken, Task> callback, params IResource[] resources)
		{
			long c = resources.Length;
			foreach (IResource resource in resources)
			{
				builder.Eventing
					.Subscribe<ResourceReadyEvent>(resource,
						async (_, token) =>
						{
							if (Interlocked.Decrement(ref c) == 0)
							{
								await callback(resources, token);
							}
						});
			}
		}
	}
}

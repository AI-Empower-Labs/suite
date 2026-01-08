using System.Reflection;

using AppHost.Extensions;
using AppHost.Services;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AppHost.Setup;

internal static class OpenTelemetryRegistration
{
	private const string OtelExporterOtlpEndpoint = "OTEL_EXPORTER_OTLP_ENDPOINT";
	private const string OtelExporterOtlpInsecure = "OTEL_EXPORTER_OTLP_INSECURE";
	private const string OtelExporterOtlpProtocol = "OTEL_EXPORTER_OTLP_PROTOCOL";
	private const string OtelExporterOtlpServiceName = "OTEL_SERVICE_NAME";
	private const string DashboardOtlpUrlVariableName = "ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL";
	private const string DashboardOtlpApiKeyVariableName = "AppHost:OtlpApiKey";

	[ResourceRegistrationOrder(100)]
	public static IResourceBuilder<OpenTelemetryCollectorResource> Register(IDistributedApplicationBuilder builder)
	{
		string? url = builder.Configuration[DashboardOtlpUrlVariableName];
		HostUrl? dashboardOtlpEndpoint = null;
		if (!string.IsNullOrEmpty(url))
		{
			url = url.Replace("+", "host.docker.internal").Replace("*", "host.docker.internal");
			if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
			{
				throw new InvalidOperationException($"Invalid dashboard OTLP endpoint URL: {url}");
			}

			dashboardOtlpEndpoint = new(uri.ToString().TrimEnd('/'));
		}

		BinaryData binaryData = BinaryData.FromStream(
			Assembly.GetExecutingAssembly().GetManifestResourceStream("AppHost.Resources.otel_config.yaml")!);
		File.WriteAllText("otel_config.yaml", binaryData.ToString());

		bool isHttpsEnabled = dashboardOtlpEndpoint?.Url.StartsWith("https", StringComparison.OrdinalIgnoreCase) ?? false;
		IResourceBuilder<OpenTelemetryCollectorResource> otel = builder
			.AddOpenTelemetryCollector(ResourceNames.OpenTelemetryName,
				settings =>
				{
					settings.EnableGrpcEndpoint = false;
					settings.EnableHttpEndpoint = true;
					settings.ForceNonSecureReceiver = true;
				})
			.WithDefaults()
			.WithEnvironment("ENVIRONMENT", builder.Environment.EnvironmentName)
			.WithConfig("./otel_config.yaml")
			.WithUrlForEndpoint("health", annotation => annotation.DisplayLocation = UrlDisplayLocation.DetailsOnly)
			.WithUrlForEndpoint("http", annotation => annotation.DisplayLocation = UrlDisplayLocation.DetailsOnly)
			.WithUrlForEndpoint("grpc", annotation => annotation.DisplayLocation = UrlDisplayLocation.DetailsOnly)
			.WithCurlHttpHealthCheckEndpoint("http://localhost:13133/health")
			.PublishAsDockerComposeService(static (_, service) =>
			{
				// Use the image requested by the compose spec
				service.Image = "otel/opentelemetry-collector-contrib:latest";
				// Map additional diagnostic ports and Prometheus exporter
				service.Ports.Add("4318:4318"); // OTLP HTTP
				service.Ports.Add("8888:8888"); // /metrics
				service.Ports.Add("9464:9464"); // Prometheus scrape exporter
				service.Ports.Add("13133:13133"); // health_check extension
			});

		if (dashboardOtlpEndpoint is not null)
		{
			otel
				.WithEnvironment("ASPIRE_ENDPOINT", dashboardOtlpEndpoint)
				.WithEnvironment("ASPIRE_API_KEY", builder.Configuration[DashboardOtlpApiKeyVariableName])
				.WithEnvironment("ASPIRE_INSECURE", isHttpsEnabled ? "false" : "true");
		}

		builder.Eventing.Subscribe<BeforeStartEvent>((e, _) =>
		{
			ILogger<OpenTelemetryCollectorResource> logger = e.Services.GetRequiredService<ILogger<OpenTelemetryCollectorResource>>();
			EndpointReference endpoint = otel.GetEndpoint("http");
			if (!endpoint.Exists)
			{
				if (logger.IsEnabled(LogLevel.Warning))
				{
					logger.LogWarning("No grpc endpoint for the collector.");
				}

				return Task.CompletedTask;
			}

			// Update all resources to forward telemetry to the collector.
			DistributedApplicationModel appModel = e.Services.GetRequiredService<DistributedApplicationModel>();
			foreach (IResource resource in appModel.Resources)
			{
				if (resource is not IResourceWithEnvironment
					&& resource is not ExecutableResource)
				{
					continue;
				}

				if (resource == otel.Resource)
				{
					continue;
				}

				resource.Annotations.Add(new EnvironmentCallbackAnnotation(context =>
				{
					if (logger.IsEnabled(LogLevel.Debug))
					{
						logger.LogDebug("Forwarding telemetry for {ResourceName} to the collector.", resource.Name);
					}

					context.EnvironmentVariables[OtelExporterOtlpEndpoint] = endpoint;
					context.EnvironmentVariables[OtelExporterOtlpInsecure] = "true";
					context.EnvironmentVariables[OtelExporterOtlpProtocol] = "http/protobuf";
					context.EnvironmentVariables[OtelExporterOtlpServiceName] = resource.Name;
				}));
			}

			return Task.CompletedTask;
		});

		return otel;
	}
}

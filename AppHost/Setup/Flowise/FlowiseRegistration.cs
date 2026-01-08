using AppHost.Extensions;
using AppHost.Services;
using AppHost.Setup.RustFs;

namespace AppHost.Setup.Flowise;

internal static class FlowiseRegistration
{
	[ResourceRegistrationOrder(300)]
	public static IResourceBuilder<FlowiseResource> Register(IDistributedApplicationBuilder builder,
		[Services.ResourceName(ResourceNames.Redis)] IResourceBuilder<RedisResource>? redis,
		[Services.ResourceName(ResourceNames.Qdrant)] IResourceBuilder<QdrantServerResource> qdrant,
		[Services.ResourceName(ResourceNames.Postgres)] IResourceBuilder<PostgresServerResource> postgres,
		[Services.ResourceName(ResourceNames.Mailpit)] IResourceBuilder<MailPitContainerResource>? smtp,
		[Services.ResourceName(ResourceNames.RustFs)] IResourceBuilder<RustFsContainerResource>? rustFsResourceBuilder,
		[Services.ResourceName(ResourceNames.OpenTelemetryName)] IResourceBuilder<OpenTelemetryCollectorResource> otelCollector)
	{
		int port = PortAllocationHelper.GetNextAvailablePort();
		IResourceBuilder<PostgresDatabaseResource> flowiseDatabase = postgres
			.AddDatabase(ResourceNames.FlowiseDatabaseDatabase, ResourceNames.FlowisePostgresDatabaseName);

		TaskCompletionSource<string> flowiseApiKeyTcs = new();
		ParameterResource parameterResource = new(ResourceNames.FlowiseApiKey,
			_ =>
			{
				flowiseApiKeyTcs.Task.Wait();
				return flowiseApiKeyTcs.Task.Result;
			},
			true);

		ParameterResource flowiseSecretKey =
			ParameterResourceBuilderExtensions.CreateDefaultPasswordParameter(builder, ResourceNames.FlowiseSecretKey, special: false);

		ParameterResource adminPasswordParameter = ParameterResourceBuilderExtensions
			.CreateDefaultPasswordParameter(builder, ResourceNames.FlowiseAdminPassword);

		IResourceBuilder<ContainerResource>? flowiseWorker = null;
		if (builder.IsProductionEnvironment)
		{
			// Only run Flowise worker in production
			flowiseWorker = builder
				.AddContainerEx(ResourceNames.FlowiseWorker, "FLOWISE_WORKER_IMAGE", "flowiseai/flowise-worker", "1.0.0")
				.WithDefaults()
				.WithHttpEndpoint(port: port, targetPort: 5566)
				.WithUrlForEndpoint("http", static url => { url.DisplayLocation = UrlDisplayLocation.DetailsOnly; })
				.WithHttpHealthCheck("/healthz", 200, "http")
				.WithPostgres(postgres, flowiseDatabase)
				.WithRedis(redis)
				.WithOpenTelemetry(otelCollector)
				.WithEnvironment("FLOWISE_SECRETKEY_OVERWRITE", flowiseSecretKey)
				.WithCurlHttpHealthCheckEndpoint("http://localhost:5566/healthz")
				.PublishAsDockerComposeService(
					static (_, service) => service.Entrypoint = ["/bin/sh", "-c", "node /app/healthcheck/healthcheck.js & sleep 5 && pnpm run start-worker"]);
		}

		FlowiseResource flowiseResource = new(ResourceNames.Flowise)
		{
			// Populate this parameter asynchronously via FlowizeIntialization once Flowise creates the API key
			ApiKeyParameter = parameterResource
		};
		IResourceBuilder<FlowiseResource> flowise = builder
			.AddResource(flowiseResource)
			.WithImageEx("FLOWISE_IMAGE", "ai-empower-labs/flowise", "1.0.0")
			.WithDefaults()
			.WithHttpEndpoint(port: port + 1, targetPort: 3000)
			.WithHttpHealthCheck("/api/v1/ping", 200, "http")
			.WithUrl("/", "Agentic Builder")
			.WithPostgres(postgres, flowiseDatabase)
			.WithRedis(redis)
			.WithOpenTelemetry(otelCollector)
			.WithEnvironment("FLOWISE_SECRETKEY_OVERWRITE", flowiseSecretKey)
			.WithEnvironment("APP_URL", flowiseResource.PrimaryEndpoint)
			.WithEnvironment("AEL_STUDIO_API_KEY", "sk-not-needed")
			.WithEnvironment("OPENAI_API_KEY", "sk-not-needed")
			.WithEnvironment("LOCAL_QDRANT_API_KEY", qdrant.Resource.ApiKeyParameter)
			.WithEnvironment("LOCAL_POSTGRES_USER", postgres.Resource.UserNameReference)
			.WithEnvironment("LOCAL_POSTGRES_PASSWORD", postgres.Resource.PasswordParameter)
			.WithEnvironment("ADMIN_USERNAME", "admin")
			.WithEnvironment("ADMIN_USER_EMAIL", "admin@aiempowerlabs.com")
			.WithEnvironment("ADMIN_USER_PASSWORD", adminPasswordParameter)
			.WithCurlHttpHealthCheckEndpoint("http://localhost:3000/api/v1/ping")
			.PublishAsDockerComposeService(
				static (_, service) => service.Entrypoint = ["/bin/sh", "-c", "sleep 3; flowise start"]);

		FlowizeIntialization.Initialize(builder, flowiseDatabase, flowiseApiKeyTcs, flowise);

		if (flowiseWorker is not null)
		{
			flowise.WithChildRelationship(flowiseWorker).WaitFor(flowiseWorker);
		}

		if (rustFsResourceBuilder is not null)
		{
			flowise.WithRustFs(rustFsResourceBuilder);
			flowiseWorker?.WithRustFs(rustFsResourceBuilder);
		}

		if (smtp is not null)
		{
			flowise.WithSmtp(smtp);
			flowiseWorker?.WithSmtp(smtp);
		}

		return flowise;
	}

	extension<T>(IResourceBuilder<T> builder) where T : IResourceWithEnvironment, IResourceWithWaitSupport
	{
		private void WithSmtp([Services.ResourceName(ResourceNames.Mailpit)] IResourceBuilder<MailPitContainerResource> smtp)
		{
			builder
				.WithReferenceRelationship(smtp).WaitFor(smtp)
				.WithEnvironment("SMTP_HOST", smtp.Resource.Host)
				.WithEnvironment("SMTP_PORT", smtp.Resource.Port)
				.WithEnvironment("SMTP_USER", "")
				.WithEnvironment("SMTP_PASSWORD", "")
				.WithEnvironment("SMTP_SECURE", "false")
				.WithEnvironment("SENDER_EMAIL", "support@aiempowerlabs.com");
		}

		private void WithRustFs(IResourceBuilder<RustFsContainerResource> rustfs)
		{
			builder
				.WithReferenceRelationship(rustfs).WaitFor(rustfs)
				.WithEnvironment("STORAGE_TYPE", "s3")
				.WithEnvironment("S3_STORAGE_BUCKET_NAME", "flowise")
				.WithEnvironment("S3_STORAGE_ACCESS_KEY_ID", rustfs.Resource.AccessKey)
				.WithEnvironment("S3_STORAGE_SECRET_ACCESS_KEY", rustfs.Resource.SecretKey)
				.WithEnvironment("S3_STORAGE_REGION", rustfs.Resource.Region)
				.WithEnvironment("S3_ENDPOINT_URL", rustfs.Resource)
				.WithEnvironment("S3_FORCE_PATH_STYLE", "true");
		}

		private IResourceBuilder<T> WithPostgres(IResourceBuilder<PostgresServerResource> postgres, IResourceBuilder<PostgresDatabaseResource> database)
		{
			ArgumentNullException.ThrowIfNull(builder);

			return builder
				// Database / Postgres
				.WithReferenceRelationship(database).WaitFor(postgres)
				.WithEnvironment("DATABASE_TYPE", "postgres")
				.WithEnvironment("DATABASE_PORT", database.Resource.Parent.PrimaryEndpoint.Property(EndpointProperty.Port))
				.WithEnvironment("DATABASE_HOST", database.Resource.Parent.PrimaryEndpoint.Property(EndpointProperty.Host))
				.WithEnvironment("DATABASE_NAME", database.Resource.DatabaseName)
				.WithEnvironment("DATABASE_USER", database.Resource.Parent.UserNameReference)
				.WithEnvironment("DATABASE_PASSWORD", database.Resource.Parent.PasswordParameter)
				.WithEnvironment("PGSSLMODE", "allow");
		}

		private IResourceBuilder<T> WithRedis(IResourceBuilder<RedisResource>? redis)
		{
			if (redis is null)
			{
				return builder;
			}

			return builder
				// Apply Redis-related environment variables to the docker compose service using Aspire resource expressions
				.WithReferenceRelationship(redis).WaitFor(redis)
				.WithEnvironment("REDIS_URL", redis.Resource.UriExpression)
				.WithEnvironment("ENABLE_BULLMQ_DASHBOARD", "true")
				.WithEnvironment("CUSTOM_MCP_PROTOCOL", "sse");
		}

		private IResourceBuilder<T> WithOpenTelemetry(IResourceBuilder<OpenTelemetryCollectorResource> otelCollector)
		{
			return builder
				.WithEnvironment("ENABLE_METRICS", "true")
				.WithEnvironment("METRICS_PROVIDER", "open_telemetry")
				.WithEnvironment("METRICS_INCLUDE_NODE_METRICS", "true")
				.WithEnvironment("METRICS_OPEN_TELEMETRY_METRIC_ENDPOINT", $"{otelCollector.Resource.HttpEndpoint}/v1/metrics")
				.WithEnvironment("METRICS_OPEN_TELEMETRY_PROTOCOL", "http")
				.WithEnvironment("METRICS_OPEN_TELEMETRY_DEBUG", "true");
		}
	}
}

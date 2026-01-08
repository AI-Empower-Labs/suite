using System.Net.Http.Json;
using System.Text.Json.Nodes;

using AppHost.Extensions;
using AppHost.Setup.Clickhouse;
using AppHost.Setup.RustFs;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AppHost.Setup.Langfuse;

internal static class LangfuseRegistration
{
	public static IResourceBuilder<LangfuseResource> Register(IDistributedApplicationBuilder builder,
		[Services.ResourceName(ResourceNames.Clickhouse)] IResourceBuilder<ClickhouseResource> clickhouse,
		[Services.ResourceName(ResourceNames.Postgres)] IResourceBuilder<PostgresServerResource> postgres,
		[Services.ResourceName(ResourceNames.RustFs)] IResourceBuilder<RustFsContainerResource> rustFs,
		[Services.ResourceName(ResourceNames.Redis)] IResourceBuilder<RedisResource> redis,
		[Services.ResourceName(ResourceNames.Mailpit)] IResourceBuilder<MailPitContainerResource> smtp,
		[Services.ResourceName(ResourceNames.OpenId)] IResourceBuilder<AiEmpowerLabsOpenIdResource> openId,
		[Services.ResourceName(ResourceNames.Studio)] IResourceBuilder<ContainerResource> studio,
		[Services.ResourceName(ResourceNames.OpenTelemetryName)] IResourceBuilder<OpenTelemetryCollectorResource> openTelemetryCollector)
	{
		int port = 3802;

		// Postgres database for Langfuse
		IResourceBuilder<PostgresDatabaseResource> langfuseDb = postgres.AddDatabase(
			ResourceNames.LangfusePostgresDatabase,
			ResourceNames.LangfusePostgresDatabaseName);

		// Secrets
		ParameterResource nextAuthSecret = ParameterResourceBuilderExtensions
			.CreateDefaultPasswordParameter(builder, ResourceNames.LangfuseNextAuthSecret);
		IResourceBuilder<ParameterResource> encryptionKey = builder
			.AddParameter(ResourceNames.LangfuseEncryptionKey, () => Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)).ToLowerInvariant(), false, true);
		ParameterResource salt = ParameterResourceBuilderExtensions
			.CreateDefaultPasswordParameter(builder, ResourceNames.LangfuseSalt);
		ParameterResource publicApiKey = ParameterResourceBuilderExtensions
			.CreateDefaultPasswordParameter(builder, ResourceNames.LangfusePublicKey);
		ParameterResource secretApiKey = ParameterResourceBuilderExtensions
			.CreateDefaultPasswordParameter(builder, ResourceNames.LangfuseSecretKey);
		ParameterResource userPassword = ParameterResourceBuilderExtensions
			.CreateDefaultPasswordParameter(builder, ResourceNames.LangfuseUserPassword);

		LangfuseResource langfuseResource = new(ResourceNames.Langfuse)
		{
			UserName = "admin",
			Email = "admin@aiempowerlabs.com",
			Password = userPassword,
			PublicKey = publicApiKey,
			SecretKey = secretApiKey
		};
		IResourceBuilder<LangfuseResource> langfuse = builder
			.AddResource(langfuseResource)
			.WithImageEx("LANGFUSE_IMAGE", "langfuse/langfuse", "3", "docker.io")
			.WithHttpEndpoint(port: port, targetPort: 3000)
			.WithExternalHttpEndpoints()
			.WithDefaults()
			.WithUrlForEndpoint("http", static url => { url.DisplayText = "Console"; })
			.WithHttpHealthCheck("/api/public/health", 200, "http")
			// Wait for dependencies
			.WithReferenceRelationship(langfuseDb).WaitFor(postgres)
			.WithReferenceRelationship(clickhouse).WaitFor(clickhouse)
			.WithReferenceRelationship(studio).WaitFor(studio)
			// Authentication Configuration ---
			// Disable Username/Password Authentication
			.WithEnvironment("AUTH_DISABLE_USERNAME_PASSWORD", "true")
			.WithEnvironment("AUTH_CUSTOM_CLIENT_ID", openId.Resource.ClientId)
			.WithEnvironment("AUTH_CUSTOM_CLIENT_SECRET", openId.Resource.ClientSecret)
			.WithEnvironment("AUTH_CUSTOM_ISSUER", openId.Resource.Issuer)
			.WithEnvironment("AUTH_CUSTOM_NAME", "AI Empower Labs")
			// Common Env
			.WithEnvironment("ENCRYPTION_KEY", encryptionKey)
			.WithEnvironment("HOSTNAME", "0.0.0.0")
			.WithEnvironment("PORT", "3000")
			// Defaults
			.WithEnvironment("LANGFUSE_DEFAULT_ORG_ID", "AI Empower Labs")
			.WithEnvironment("LANGFUSE_DEFAULT_ORG_ROLE", "OWNER")
			.WithEnvironment("LANGFUSE_DEFAULT_PROJECT_ROLE", "OWNER")
			.WithEnvironment("LANGFUSE_INIT_ORG_ID", "AI Empower Labs")
			.WithEnvironment("LANGFUSE_INIT_ORG_NAME", "AI Empower Labs")
			.WithEnvironment("LANGFUSE_INIT_PROJECT_ID", "Default")
			.WithEnvironment("LANGFUSE_INIT_PROJECT_NAME", "Default")
			.WithEnvironment("LANGFUSE_INIT_PROJECT_RETENTION", "30")
			.WithEnvironment("LANGFUSE_INIT_PROJECT_PUBLIC_KEY", publicApiKey)
			.WithEnvironment("LANGFUSE_INIT_PROJECT_SECRET_KEY", secretApiKey)
			.WithEnvironment("LANGFUSE_INIT_USER_EMAIL", langfuseResource.Email)
			.WithEnvironment("LANGFUSE_INIT_USER_NAME", langfuseResource.UserName)
			.WithEnvironment("LANGFUSE_INIT_USER_PASSWORD", langfuseResource.Password)
			.WithCurlHttpHealthCheckEndpoint("http://localhost:3000/api/public/health");

		// Apply common env after creation to keep fluent chain intact
		langfuse = WithLangfuseCommonEnv(langfuse);

		langfuse.OnResourceReady((resource, readyEvent, token) =>
			CreateLlmConnection(
				readyEvent.Services.GetRequiredService<ILoggerFactory>().CreateLogger("LangfuseInitialization"),
				resource,
				token));

		// Add Langfuse Worker container with shared environment
		IResourceBuilder<ContainerResource> langfuseWorker = builder
			.AddContainerEx(ResourceNames.LangfuseWorker, "LANGFUSE_WORKER_IMAGE", "langfuse/langfuse-worker", "3", "docker.io")
			.WithHttpEndpoint(port: port + 1, targetPort: 3030)
			.WithUrlForEndpoint("http", static url => { url.DisplayLocation = UrlDisplayLocation.DetailsOnly; })
			.WithReferenceRelationship(langfuseDb).WaitFor(postgres)
			.WithReferenceRelationship(clickhouse).WaitFor(clickhouse);

		langfuseWorker = WithLangfuseCommonEnv(langfuseWorker);

		langfuse.WithChildRelationship(langfuseWorker).WaitFor(langfuseWorker);

		openTelemetryCollector
			.WithEnvironment("LANGFUSE_OTEL_ENDPOINT",
				$"http://{langfuse.Resource.GetEndpoint("http").Property(EndpointProperty.HostAndPort)}/api/public/otel");

		openTelemetryCollector.Resource.Annotations.Add(new EnvironmentCallbackAnnotation(async context =>
		{
			try
			{
				string? token = await langfuse.Resource.GetAuthenticationTokenBase64(context.CancellationToken);
				context.EnvironmentVariables["LANGFUSE_AUTH_KEY"] = $"Basic {token}";
			}
			catch (Exception ex)
			{
				if (context.Logger.IsEnabled(LogLevel.Warning))
				{
					context.Logger.LogWarning(ex, "Failed to resolve Langfuse auth token for OTEL collector.");
				}
			}
		}));

		return langfuse;

		// Helper to add the common Langfuse environment variables (shared between web and worker)
		IResourceBuilder<T> WithLangfuseCommonEnv<T>(IResourceBuilder<T> rb) where T : IResourceWithEnvironment, IResourceWithWaitSupport
		{
			// Postgres (primary DB)
			rb = rb
				.WithEnvironment("DATABASE_URL", langfuseDb.Resource.UriExpression)
				// ClickHouse
				.WithEnvironment("CLICKHOUSE_URL",
					$"http://{clickhouse.Resource.PrimaryEndpoint.Property(EndpointProperty.Host)}:{clickhouse.Resource.PrimaryEndpoint.Property(EndpointProperty.Port)}")
				.WithEnvironment("CLICKHOUSE_USER", clickhouse.Resource.UserName)
				.WithEnvironment("CLICKHOUSE_PASSWORD", clickhouse.Resource.Password)
				.WithEnvironment("CLICKHOUSE_CLUSTER_ENABLED", "false")
				// Migration URL uses native protocol on port 9000
				.WithEnvironment("CLICKHOUSE_MIGRATION_URL",
					$"clickhouse://{clickhouse.Resource.GetEndpoint("migration").Property(EndpointProperty.Host)}:{clickhouse.Resource.GetEndpoint("migration").Property(EndpointProperty.Port)}")
				// General config
				.WithEnvironment("TELEMETRY_ENABLED", "true")
				.WithEnvironment("LANGFUSE_ENABLE_EXPERIMENTAL_FEATURES", "true")
				//.WithEnvironment("NEXTAUTH_URL", langfuse.Resource.GetEndpoint("http"))
				// Explicitly set external host to localhost using the allocated port
				.WithEnvironment("NEXTAUTH_URL", "http://localhost:" + port)
				.WithEnvironment("NEXTAUTH_SECRET", nextAuthSecret)
				.WithEnvironment("SALT", salt)
				.WithEnvironment("CLICKHOUSE_DISABLE_SSL", "true");

			// S3/RustFs integration (used for events, media, and batch export)
			rb = rb
				.WithReferenceRelationship(rustFs).WaitFor(rustFs)
				.WithEnvironment("LANGFUSE_S3_EVENT_UPLOAD_BUCKET", ResourceNames.LangfuseBucketName)
				.WithEnvironment("LANGFUSE_S3_EVENT_UPLOAD_REGION", rustFs.Resource.Region)
				.WithEnvironment("LANGFUSE_S3_EVENT_UPLOAD_ACCESS_KEY_ID", rustFs.Resource.AccessKey)
				.WithEnvironment("LANGFUSE_S3_EVENT_UPLOAD_SECRET_ACCESS_KEY", rustFs.Resource.SecretKey)
				.WithEnvironment("LANGFUSE_S3_EVENT_UPLOAD_ENDPOINT", rustFs.Resource)
				.WithEnvironment("LANGFUSE_S3_EVENT_UPLOAD_FORCE_PATH_STYLE", "true")
				.WithEnvironment("LANGFUSE_S3_EVENT_UPLOAD_PREFIX", "events/")
				.WithEnvironment("LANGFUSE_S3_MEDIA_UPLOAD_BUCKET", ResourceNames.LangfuseBucketName)
				.WithEnvironment("LANGFUSE_S3_MEDIA_UPLOAD_REGION", rustFs.Resource.Region)
				.WithEnvironment("LANGFUSE_S3_MEDIA_UPLOAD_ACCESS_KEY_ID", rustFs.Resource.AccessKey)
				.WithEnvironment("LANGFUSE_S3_MEDIA_UPLOAD_SECRET_ACCESS_KEY", rustFs.Resource.SecretKey)
				.WithEnvironment("LANGFUSE_S3_MEDIA_UPLOAD_ENDPOINT", rustFs.Resource)
				.WithEnvironment("LANGFUSE_S3_MEDIA_UPLOAD_FORCE_PATH_STYLE", "true")
				.WithEnvironment("LANGFUSE_S3_MEDIA_UPLOAD_PREFIX", "media/")
				.WithEnvironment("LANGFUSE_S3_BATCH_EXPORT_ENABLED", "false")
				.WithEnvironment("LANGFUSE_S3_BATCH_EXPORT_BUCKET", ResourceNames.LangfuseBucketName)
				.WithEnvironment("LANGFUSE_S3_BATCH_EXPORT_PREFIX", "exports/")
				.WithEnvironment("LANGFUSE_S3_BATCH_EXPORT_REGION", rustFs.Resource.Region)
				.WithEnvironment("LANGFUSE_S3_BATCH_EXPORT_ENDPOINT", rustFs.Resource)
				.WithEnvironment("LANGFUSE_S3_BATCH_EXPORT_EXTERNAL_ENDPOINT", rustFs.Resource)
				.WithEnvironment("LANGFUSE_S3_BATCH_EXPORT_ACCESS_KEY_ID", rustFs.Resource.AccessKey)
				.WithEnvironment("LANGFUSE_S3_BATCH_EXPORT_SECRET_ACCESS_KEY", rustFs.Resource.SecretKey)
				.WithEnvironment("LANGFUSE_S3_BATCH_EXPORT_FORCE_PATH_STYLE", "true");

			// Ingestion tuning (optional, leave empty defaults)
			rb = rb
				.WithEnvironment("LANGFUSE_INGESTION_QUEUE_DELAY_MS", "")
				.WithEnvironment("LANGFUSE_INGESTION_CLICKHOUSE_WRITE_INTERVAL_MS", "");

			// Redis (optional in this deployment)
			rb = rb
				.WithReferenceRelationship(redis).WaitFor(redis)
				.WithEnvironment("REDIS_HOST", redis.Resource.PrimaryEndpoint.Property(EndpointProperty.Host))
				.WithEnvironment("REDIS_PORT", redis.Resource.PrimaryEndpoint.Property(EndpointProperty.Port))
				.WithEnvironment("REDIS_USERNAME", "")
				.WithEnvironment("REDIS_AUTH", redis.Resource.PasswordParameter!)
				.WithEnvironment("REDIS_TLS_ENABLED", "false");

			rb = rb
				.WithReferenceRelationship(smtp).WaitFor(smtp)
				.WithEnvironment("SMTP_CONNECTION_URL", smtp.Resource.UriExpression);

			return rb;
		}

		async Task CreateLlmConnection(
			ILogger logger,
			LangfuseResource resource,
			CancellationToken token)
		{
			try
			{
				using HttpClient client = new();
				client.DefaultRequestHeaders.Authorization = await resource.GetAuthenticationHeader(token);
				string requestUri = $"{await resource.PrimaryEndpoint.GetValueAsync(token)}/api/public/llm-connections";
				string? studioPort = await studio.Resource.GetEndpoint("http").Property(EndpointProperty.TargetPort).GetValueAsync(token);
				string baseUrl = $"http://{studio.Resource.Name}:{studioPort}/v1";

				// Retrieve models from llm provider and populate
				List<string> customModels = [];
				try
				{
					string? studioHost = await studio.Resource.GetEndpoint("http").GetValueAsync(token);
					JsonNode? modelsResponse = await client.GetFromJsonAsync<JsonNode>($"{studioHost}/v1/models", token);
					if (modelsResponse?["data"] is JsonArray data)
					{
						foreach (JsonNode? item in data)
						{
							string? id = item?["id"]?.GetValue<string>();
							if (!string.IsNullOrEmpty(id))
							{
								customModels.Add(id);
							}
						}
					}
				}
				catch (Exception ex)
				{
					logger.LogWarning(ex, "Failed to retrieve models from Studio.");
				}

				HttpResponseMessage result = await client
					.PutAsJsonAsync(
						requestUri,
						new
						{
							provider = "AI Empower Labs",
							adapter = "openai",
							secretKey = "sk-not-needed",
							baseURL = baseUrl,
							withDefaultModels = false,
							customModels
						}, cancellationToken: token);
				result.EnsureSuccessStatusCode();
			}
			catch (Exception e)
			{
				logger.LogError(e, "Failed to create LLM connection for Langfuse.");
			}
		}
	}
}

using AppHost.Extensions;
using AppHost.Services;

namespace AppHost.Setup;

// ReSharper disable once InconsistentNaming
internal static class n8nRegistration
{
	[ResourceRegistrationOrder(200)]
	public static IResourceBuilder<ContainerResource>? Register(IDistributedApplicationBuilder builder,
		[Services.ResourceName(ResourceNames.Postgres)] IResourceBuilder<PostgresServerResource> postgres,
		[Services.ResourceName(ResourceNames.Redis)] IResourceBuilder<RedisResource>? redis)
	{
		if (!builder.StartN8N)
		{
			return null;
		}

		IResourceBuilder<PostgresDatabaseResource> n8nDatabase = postgres
			.AddDatabase(ResourceNames.n8nPostgresDatabase, ResourceNames.n8nPostgresDatabaseName);
		IResourceBuilder<ContainerResource> n8n = builder
			.AddContainerEx(ResourceNames.N8N, "N8N_IMAGE", "n8nio/n8n", "2.0.0", "docker.io")
			.WithDefaults()
			.WithHttpEndpoint(port: PortAllocationHelper.GetNextAvailablePort(), targetPort: 5678)
			.WithUrlForEndpoint("http", static url => { url.DisplayText = "n8n Dashboard"; })
			.WithHttpHealthCheck("/healthz", 200, "http")
			.WithEnvironment("GENERIC_TIMEZONE", "Europe/stockholm")
			.WithEnvironment("TZ", "Europe/stockholm")
			.WithEnvironment("N8N_PERSONALIZATION_ENABLED", "false")
			.WithEnvironment("N8N_VERSION_NOTIFICATIONS_ENABLED", "false")
			.WithEnvironment("N8N_DIAGNOSTICS_ENABLED", "false")
			.WithEnvironment("N8N_HIRING_BANNER_ENABLED", "false")
			.WithEnvironment("NODE_ENV", "production")
			// Postgres
			.WithEnvironment("DB_TYPE", "postgresdb")
			.WithEnvironment("DB_POSTGRESDB_DATABASE", n8nDatabase.Resource.DatabaseName)
			.WithEnvironment("DB_POSTGRESDB_HOST", postgres.Resource.PrimaryEndpoint.Property(EndpointProperty.Host))
			.WithEnvironment("DB_POSTGRESDB_PORT", postgres.Resource.PrimaryEndpoint.Property(EndpointProperty.Port))
			.WithEnvironment("DB_POSTGRESDB_USER", postgres.Resource.UserNameReference)
			.WithEnvironment("DB_POSTGRESDB_SCHEMA", "public")
			.WithEnvironment("DB_POSTGRESDB_PASSWORD", postgres.Resource.PasswordParameter)
			.WithReferenceRelationship(n8nDatabase).WaitFor(postgres)
			.WithCurlHttpHealthCheckEndpoint("http://localhost:5678/healthz");

		if (redis is not null)
		{
			n8n
				.WithEnvironment("QUEUE_BULL_PREFIX", "n8n_")
				.WithEnvironment("QUEUE_BULL_REDIS_DB", "2")
				.WithEnvironment("QUEUE_BULL_REDIS_HOST", redis.Resource.PrimaryEndpoint.Property(EndpointProperty.Host))
				.WithEnvironment("QUEUE_BULL_REDIS_PORT", redis.Resource.PrimaryEndpoint.Property(EndpointProperty.Port))
				.WithEnvironment("QUEUE_BULL_REDIS_USERNAME", "")
				.WithEnvironment("QUEUE_BULL_REDIS_PASSWORD", redis.Resource.PasswordParameter!)
				.WithReferenceRelationship(redis).WaitFor(redis);
		}

		return n8n;
	}
}

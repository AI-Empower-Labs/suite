using AppHost.Extensions;
using AppHost.Services;
using AppHost.Setup.RustFs;
using Aspire.Hosting.Docker.Resources.ServiceNodes;

namespace AppHost.Setup;

internal static class PostgresRegistration
{
	[ResourceRegistrationOrder(100)]
	public static IResourceBuilder<PostgresServerResource> Register(IDistributedApplicationBuilder builder,
		[Services.ResourceName(ResourceNames.RustFs)] IResourceBuilder<RustFsContainerResource> rustFsResourceBuilder)
	{
		int port = PortAllocationHelper.GetNextAvailablePort();
		IResourceBuilder<PostgresServerResource> postgres = builder
			.AddPostgres(ResourceNames.Postgres, port: port)
			.WithDefaults()
			.WithImageEx("POSTGRES_IMAGE", "pgvector/pgvector", "pg17", "docker.io")
			.WithIconName("fluent:database-16-regular")
			.WithUrlForEndpoint("tcp", static url => { url.DisplayLocation = UrlDisplayLocation.DetailsOnly; })
			.WithPgWeb(resourceBuilder => resourceBuilder
				.WithDefaults()
				.WithIconName("fluent:database-16-regular")
				.WithUrlForEndpoint("http", static url => { url.DisplayText = "PgWeb Dashboard"; }))
			.WithDataVolume()
			.PublishAsDockerComposeService((_, service) =>
			{
				service.Healthcheck = new Healthcheck
				{
					Test = ["CMD-SHELL", "pg_isready", "-d", "db_prod"],
					Interval = "10s",
					Timeout = "5s",
					Retries = 5,
					StartPeriod = "30s"
				};
				service.Environment.Add("POSTGRES_DB", "flowise");
				service.Environment.Add("POSTGRES_MULTIPLE_DATABASES", "librechat,ael");
			});

		if (builder.ExecutionContext.IsPublishMode)
		{
			postgres
				.WithBindMount("./init-db.sh", "/docker-entrypoint-initdb.d/init-db.sh", true);
		}

		// Optional: add automated S3 backups if RustFS credentials are provided
		if (builder.IsProductionEnvironment)
		{
			builder
				.AddContainer("postgres-backup-s3", "itbm/postgres-backup-s3", "1.2")
				// make sure this sidecar waits for Postgres to be ready
				.WithReferenceRelationship(postgres).WaitFor(postgres)
				// Database connection
				.WithEnvironment("POSTGRES_HOST", postgres.Resource.PrimaryEndpoint.Property(EndpointProperty.Host))
				.WithEnvironment("POSTGRES_PORT", postgres.Resource.PrimaryEndpoint.Property(EndpointProperty.Port))
				.WithEnvironment("POSTGRES_DATABASE", "all")
				.WithEnvironment("POSTGRES_USER", postgres.Resource.UserNameReference)
				.WithEnvironment("POSTGRES_PASSWORD", postgres.Resource.PasswordParameter)
				// S3 (RustFS) configuration
				.WithEnvironment("S3_REGION", rustFsResourceBuilder.Resource.Region)
				.WithEnvironment("S3_ACCESS_KEY_ID", rustFsResourceBuilder.Resource.AccessKey)
				.WithEnvironment("S3_SECRET_ACCESS_KEY", rustFsResourceBuilder.Resource.SecretKey)
				.WithEnvironment("S3_BUCKET", ResourceNames.RustFsPostgresBackupBucketName)
				.WithEnvironment("S3_PREFIX", "daily")
				.WithEnvironment("S3_ENDPOINT", "http://rustfs:9000")
				.WithEnvironment("S3_S3V4", "yes")
				.WithEnvironment("S3_FORCE_PATH_STYLE", "true")
				// Backup schedule: daily at 02:00doc
				.WithEnvironment("SCHEDULE", "0 2 * * *")
				// Keep last 7 backups
				.WithEnvironment("BACKUP_KEEP_DAYS", "31")
				.WithIconName("fluent:cloud-backup-24-regular")
				.PublishAsDockerComposeService((_, service) =>
				{
					service.Labels.Add("restore-instructions",
						"""
						To restore from a backup, run the following command:
						docker run --rm --network suite_default \
						  -e S3_REGION=<region> \
						  -e S3_ACCESS_KEY_ID=<key> \
						  -e S3_SECRET_ACCESS_KEY=<secret> \
						  -e S3_BUCKET=postgres-backups \
						  -e S3_PREFIX=daily \
						  -e S3_ENDPOINT=http://rustfs:9000 \
						  -e S3_S3V4=yes \
						  -e S3_FORCE_PATH_STYLE=true \
						  -e BACKUP_FILE=daily/<dbname>_YYYY-MM-DDTHH:MM:SSZ.sql.gz \
						  -e POSTGRES_DATABASE=<dbname> \
						  -e POSTGRES_USER=<user> \
						  -e POSTGRES_PASSWORD=<password> \
						  -e POSTGRES_HOST=postgres \
						  -e CREATE_DATABASE=yes \
						  itbm/postgres-backup-s3
						""");
					service.Healthcheck = new Healthcheck
					{
						Test =
						[
							"CMD-SHELL",
							$"sh -c 'pg_isready -U {postgres.Resource.UserNameReference} -d {postgres.Resource.PasswordParameter}'"
						],
						Interval = "30s",
						Timeout = "10s",
						Retries = 5,
						StartPeriod = "15s"
					};
				});
		}

		return postgres;
	}

	public static void Register(
		[Services.ResourceName(ResourceNames.Postgres)] IResourceBuilder<PostgresServerResource> postgres,
		[Services.ResourceName(ResourceNames.OpenTelemetryName)] IResourceBuilder<OpenTelemetryCollectorResource> otelCollector)
	{
		otelCollector.WithEnvironment("POSTGRES_ENDPOINT", postgres.Resource.PrimaryEndpoint.Property(EndpointProperty.HostAndPort));
		otelCollector.WithEnvironment("POSTGRES_USER", postgres.Resource.UserNameReference);
		otelCollector.WithEnvironment("POSTGRES_PASSWORD", postgres.Resource.PasswordParameter);
		foreach ((int Index, KeyValuePair<string, string> Item) database in postgres.Resource.Databases.Index())
		{
			otelCollector.WithEnvironment($"POSTGRES_DATABASE_{database.Index}", database.Item.Value);
		}
	}
}

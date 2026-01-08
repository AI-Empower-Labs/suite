using System.Reflection;

using AppHost.Extensions;
using AppHost.Services;

using Aspire.Hosting.Docker.Resources.ServiceNodes;

namespace AppHost.Setup;

internal static class LibreChatRegistration
{
	[ResourceRegistrationOrder(600)]
	public static IResourceBuilder<ContainerResource> Register(IDistributedApplicationBuilder builder,
		[Services.ResourceName(ResourceNames.Studio)] IResourceBuilder<ContainerResource> studio,
		[Services.ResourceName(ResourceNames.Redis)] IResourceBuilder<RedisResource>? redis,
		[Services.ResourceName(ResourceNames.Postgres)] IResourceBuilder<PostgresServerResource> postgres,
		[Services.ResourceName(ResourceNames.EmbeddingModel)] ParameterResource embeddingModel,
		[Services.ResourceName(ResourceNames.OpenId)] IResourceBuilder<AiEmpowerLabsOpenIdResource> openId,
		[Services.ResourceName(ResourceNames.OpenTelemetryName)] IResourceBuilder<OpenTelemetryCollectorResource> otelCollector,
		[Services.ResourceName(ResourceNames.Mailpit)] IResourceBuilder<MailPitContainerResource>? smtp)
	{
		BinaryData binaryData = BinaryData.FromStream(
			Assembly.GetExecutingAssembly().GetManifestResourceStream("AppHost.Resources.librechat.yaml")!);
		File.WriteAllText("librechat.yaml", binaryData.ToString());

		int port = PortAllocationHelper.GetNextAvailablePort();
		IResourceBuilder<MongoDBServerResource> mongoDb = builder
			.AddMongoDB(ResourceNames.MongoDb, port: port++)
			.WithDefaults()
			.WithMongoExpress(resourceBuilder => resourceBuilder.WithHostPort(port++))
			.WithUrlForEndpoint("tcp", static url => { url.DisplayLocation = UrlDisplayLocation.DetailsOnly; })
			.WithDataVolume()
			.PublishAsDockerComposeService((_, service) =>
			{
				service.Healthcheck = new Healthcheck
				{
					Test = ["CMD", "mongosh", "--eval", "db.adminCommand('ping')"],
					Interval = "10s",
					Timeout = "5s",
					Retries = 5,
					StartPeriod = "30s"
				};
			});
		otelCollector.WithEnvironment("MONGODB_ENDPOINT", mongoDb.Resource.PrimaryEndpoint.Property(EndpointProperty.HostAndPort));
		otelCollector.WithEnvironment("MONGODB_USER", mongoDb.Resource.UserNameReference);
		otelCollector.WithEnvironment("MONGODB_PASSWORD", mongoDb.Resource.PasswordParameter!);

		IResourceBuilder<MongoDBDatabaseResource> libreChatDatabaseMongoDb = mongoDb
			.AddDatabase(ResourceNames.LibreChatMongoDatabase, ResourceNames.LibreChatPostgresDatabaseName);

		IResourceBuilder<PostgresDatabaseResource> libreChatDatabase = postgres
			.AddDatabase(ResourceNames.LibreChatPostgresDatabase, ResourceNames.LibreChatPostgresDatabaseName);
		IResourceBuilder<ParameterResource> jwtSecret = builder
			.AddParameter(ResourceNames.LangfuseJwtSecret, "71e417087ea6c2b948cb0ab09331dd4af2931059e6579e735ec447be41c0b163", secret: true);
		IResourceBuilder<ParameterResource> jwtRefreshSecret = builder
			.AddParameter(ResourceNames.LibreChatJwtRefreshSecret, "306c862dba9342bc79a2e5f88e671f46bdc82d3841faecdd5dec566fa09d9d26", secret: true);
		IResourceBuilder<ParameterResource> credsKey = builder
			.AddParameter(ResourceNames.LibreChatCredsKey, "069faa7fc639daf9506d02b11a521c11781d877be4faf35bfd8336895cceead9", secret: true);
		IResourceBuilder<ParameterResource> credsIv = builder
			.AddParameter(ResourceNames.LibreChatCredsIv, "1211786c8e0f6d3bb28568376ae8b0ab", secret: true);

		IResourceBuilder<ContainerResource> ragApi = builder
			.AddContainerEx(ResourceNames.LibreChatRag, "LIBRECHAT_RAG_IMAGE", "danny-avila/librechat-rag-api-dev-lite", "latest")
			.WithDefaults()
			.WithHttpEndpoint(port: port++, targetPort: 8000)
			.WithUrlForEndpoint("http", static url => { url.DisplayLocation = UrlDisplayLocation.DetailsOnly; })
			.WithHttpHealthCheck("/health", 200, "http")
			.WithReferenceRelationship(mongoDb).WaitFor(mongoDb)
			.WithReferenceRelationship(libreChatDatabase).WaitFor(libreChatDatabase)
			.WithReferenceRelationship(studio).WaitFor(studio)
			.WithEnvironment("HOST", "0.0.0.0")
			.WithEnvironment("RAG_PORT", "8000")
			.WithEnvironment("MONGO_URI", libreChatDatabaseMongoDb.Resource.UriExpression)
			// Database setup
			.WithEnvironment("DB_HOST", postgres.Resource.PrimaryEndpoint.Property(EndpointProperty.Host))
			.WithEnvironment("DB_PORT", postgres.Resource.PrimaryEndpoint.Property(EndpointProperty.Port))
			.WithEnvironment("POSTGRES_DB", libreChatDatabase.Resource.DatabaseName)
			.WithEnvironment("POSTGRES_USER", postgres.Resource.UserNameReference)
			.WithEnvironment("POSTGRES_PASSWORD", postgres.Resource.PasswordParameter)
			// LLM
			.WithEnvironment("CHUNK_SIZE", "2048")
			.WithEnvironment("CHUNK_OVERLAP", "200")
			.WithEnvironment("OPENAI_BASEURL", $"{studio.Resource.GetEndpoint("http")}/v1")
			.WithEnvironment("OPENAI_API_KEY", "sk-not-needed")
			.WithEnvironment("EMBEDDINGS_PROVIDER", "openai")
			.WithEnvironment("EMBEDDINGS_MODEL", embeddingModel)
			.WithCurlHttpHealthCheckEndpoint("http://localhost:8000/health");

		IResourceBuilder<MeilisearchResource> meiliSearch = builder
			.AddMeilisearch(ResourceNames.MeiliSearch, port: port++)
			.WithDefaults()
			.WithUrlForEndpoint("http", static url => { url.DisplayLocation = UrlDisplayLocation.DetailsOnly; })
			.WithEnvironment("MEILI_NO_ANALYTICS", "true")
			.WithEnvironment("MEILI_MAX_INDEXING_MEMORY", "500mb")
			.WithEnvironment("MEILI_MAX_INDEXING_THREADS", "2")
			.WithEnvironment("MEILI_EXPERIMENTAL_MAX_NUMBER_OF_BATCHED_TASKS", "5")
			.WithDataVolume()
			.WithCurlHttpHealthCheckEndpoint("http://localhost:7700/health");

		IResourceBuilder<ContainerResource>? codeSandbox = null;
		if (builder.IsProductionEnvironment)
		{
			codeSandbox = builder
				.AddContainerEx(ResourceNames.CodeSandbox, "CODESANDBOX_IMAGE", "librechat-ai/codesandbox-client/bundler", "latest")
				.WithDefaults()
				.WithHttpEndpoint(port: port++, targetPort: 80)
				.WithUrlForEndpoint("http", static url => { url.DisplayLocation = UrlDisplayLocation.DetailsOnly; })
				.WithCurlHttpHealthCheckEndpoint("http://localhost/");
		}

		IResourceBuilder<ContainerResource> libreChatResourceBuilder = builder
			.AddContainerEx(ResourceNames.LibreChat, "LIBRECHAT_IMAGE", "ai-empower-labs/librechat", "1.0.0")
			.WithHttpEndpoint(port: 3080, targetPort: 3080).WithUrl("/", "Chatflow Builder")
			.WithExternalHttpEndpoints()
			.WithHttpHealthCheck("/health", 200, "http")
			.WithChildRelationship(mongoDb).WaitFor(mongoDb)
			.WithChildRelationship(ragApi).WaitFor(ragApi)
			.WithChildRelationship(meiliSearch).WaitFor(meiliSearch)
			.WithReferenceRelationship(studio).WaitFor(studio)
			.WithEnvironment("HOST", "0.0.0.0")
			.WithEnvironment("MONGO_URI", libreChatDatabaseMongoDb.Resource.UriExpression)
			.WithEnvironment("SEARCH", "true")
			.WithEnvironment("MEILI_HOST", meiliSearch.Resource.PrimaryEndpoint)
			.WithEnvironment("MEILI_MASTER_KEY", meiliSearch.Resource.MasterKeyParameter)
			.WithEnvironment("RAG_PORT", "8000")
			.WithEnvironment("RAG_API_URL", ragApi.Resource.GetEndpoint("http"))
			.WithEnvironment("NODE_ENV", "production")
			.WithEnvironment("NO_INDEX", "true")
			.WithEnvironment("LOGIN_MAX", "20")
			.WithEnvironment("ALLOW_EMAIL_LOGIN", "true")
			.WithEnvironment("ALLOW_REGISTRATION", "true")
			.WithEnvironment("ALLOW_SOCIAL_LOGIN", "true")
			.WithEnvironment("ALLOW_SOCIAL_REGISTRATION", "false")
			.WithEnvironment("ALLOW_PASSWORD_RESET", "false")
			.WithEnvironment("ALLOW_ACCOUNT_DELETION", "true")
			.WithEnvironment("ALLOW_UNVERIFIED_EMAIL_LOGIN", "false")
			.WithEnvironment("JWT_SECRET", jwtSecret)
			.WithEnvironment("JWT_REFRESH_SECRET", jwtRefreshSecret)
			.WithEnvironment("CREDS_KEY", credsKey)
			.WithEnvironment("CREDS_IV", credsIv)
			// LLM
			.WithEnvironment("AEL_BASE_URL", $"{studio.Resource.GetEndpoint("http")}/v1")
			.WithEnvironment("AEL_API_KEY", "sk-not-needed")
			.WithEnvironment("AEL_STUDIO_MCP_URL", $"{studio.Resource.GetEndpoint("http")}/mcp")
			// OIDC
			.WithEnvironment("SESSION_EXPIRY", "1000 * 60 * 15")
			.WithEnvironment("REFRESH_TOKEN_EXPIRY", "(1000 * 60 * 60 * 24) * 7")
			// AEL Auth
			.WithEnvironment("OPENID_CLIENT_ID", openId.Resource.ClientId)
			.WithEnvironment("OPENID_CLIENT_SECRET", openId.Resource.ClientSecret)
			.WithEnvironment("OPENID_ISSUER", openId.Resource.OpenIdConfiguration)
			.WithEnvironment("OPENID_SCOPE", "openid profile email")
			.WithEnvironment("OPENID_SESSION_SECRET", ParameterResourceBuilderExtensions.CreateDefaultPasswordParameter(builder, "oidc-session-secret").Default!.GetDefaultValue())
			.WithEnvironment("OPENID_CALLBACK_URL", "/oauth/openid/callback")
			.WithEnvironment("OPENID_USERNAME_CLAIM", "name")
			.WithEnvironment("OPENID_NAME_CLAIM", "name")
			.WithEnvironment("OPENID_USE_END_SESSION_ENDPOINT", "true")
			.WithEnvironment("OPENID_AUTO_REDIRECT", "true")
			.WithEnvironment("OPENID_BUTTON_LABEL", "AI Empower Labs Authentication")
			.WithEnvironment("OPENID_IMAGE_URL", "https://content.aiempowerlabs.com/ael/logo.png")
			.WithEnvironment("OPENID_GENERATE_NONCE", "true")

			// LibreChat
			.WithEnvironment("ENDPOINTS", "agents,custom")
			.WithEnvironment("TITLE_CONVO", "true")
			.WithEnvironment("DOMAIN_CLIENT", "http://localhost:3080")
			.WithEnvironment("DOMAIN_SERVER", "http://localhost:3080")
			.WithEnvironment("ALLOW_SHARED_LINKS", "true")
			.WithEnvironment("ALLOW_SHARED_LINKS_PUBLIC", "true")
			.WithEnvironment("APP_TITLE", "AI Empower Labs")
			.WithEnvironment("CUSTOM_FOOTER", "AI Empower Labs")
			.WithEnvironment("HELP_AND_FAQ_URL", "https://www.aiempowerlabs.com")
			.WithBindMount("./librechat.yaml", "/app/librechat.yaml", true)
			.WithBindMount("./images", "/app/client/public/images")
			.WithBindMount("./uploads", "/app/uploads")
			.WithBindMount("./logs", "/app/api/logs")
			.WithWgetHttpHealthCheckEndpoint("http://0.0.0.0:3080/health");

		if (codeSandbox is not null)
		{
			libreChatResourceBuilder
				.WithChildRelationship(codeSandbox).WaitFor(codeSandbox)
				.WithEnvironment("SANDPACK_BUNDLER_URL", codeSandbox.Resource.GetEndpoint("http"));
		}

		if (smtp is not null)
		{
			libreChatResourceBuilder
				.WithEnvironment("EMAIL_USERNAME", string.Empty)
				.WithEnvironment("EMAIL_PASSWORD", string.Empty)
				.WithEnvironment("EMAIL_HOST", smtp.Resource.Host)
				.WithEnvironment("EMAIL_PORT", smtp.Resource.Port)
				.WithEnvironment("EMAIL_ALLOW_SELFSIGNED", "true");
		}

		if (redis is not null)
		{
			libreChatResourceBuilder
				.WithEnvironment("USE_REDIS", "true")
				.WithEnvironment("REDIS_URI", $"redis://:{redis.Resource.PasswordParameter!}@{redis.Resource.PrimaryEndpoint.Property(EndpointProperty.HostAndPort)}")
				.WithReferenceRelationship(redis).WaitFor(redis);
		}

		return libreChatResourceBuilder;
	}
}

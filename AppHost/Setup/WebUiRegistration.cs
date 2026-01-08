using AppHost.Extensions;

using Microsoft.Extensions.Configuration;

namespace AppHost.Setup;

internal static class WebUiRegistration
{
	public static IResourceBuilder<ContainerResource>? Register(IDistributedApplicationBuilder builder,
		[Services.ResourceName(ResourceNames.Studio)] IResourceBuilder<ContainerResource> studio,
		[Services.ResourceName(ResourceNames.SearXng)] IResourceBuilder<ContainerResource> searXng,
		[Services.ResourceName(ResourceNames.Qdrant)] IResourceBuilder<QdrantServerResource> qdrant,
		[Services.ResourceName(ResourceNames.EmbeddingModel)] ParameterResource embeddingModel,
		[Services.ResourceName(ResourceNames.OpenId)] IResourceBuilder<AiEmpowerLabsOpenIdResource> openId)
	{
		string webUiEnabledString = builder.Configuration.GetValue<string>("WEBUI_ENABLED") ?? "false";
		if (!bool.TryParse(webUiEnabledString, out bool webUiEnabled)
			|| !webUiEnabled)
		{
			return null;
		}

		// Generate a secure secret for Open WebUI session/signing
		ParameterResource webUiSecretKey = ParameterResourceBuilderExtensions
			.CreateDefaultPasswordParameter(builder, "webui-secret-key");

		int port = 3081;
		return builder
			.AddContainerEx(ResourceNames.WebUI, "WEBUI_IMAGE", "open-webui/open-webui", "latest")
			.WithDefaults()
			.WithHttpEndpoint(port: port, targetPort: 8080)
			.WithHttpHealthCheck("/health", 200, "http")
			.WithUrlForEndpoint("http", static url => { url.DisplayText = "Chat"; })
			.WithReferenceRelationship(studio).WaitFor(studio)
			.WithReferenceRelationship(searXng).WaitFor(searXng)
			.WithReferenceRelationship(qdrant).WaitFor(qdrant)
			.WithEnvironment("ENABLE_SIGNUP", "false")
			.WithEnvironment("ENABLE_SIGNUP_PASSWORD_CONFIRMATION", "false")
			.WithEnvironment("ENABLE_LOGIN_FORM", "false")
			.WithEnvironment("ENABLE_PASSWORD_AUTH", "false")
			.WithEnvironment("DEFAULT_USER_ROLE", "admin")
			.WithEnvironment("WEBUI_NAME", "AI Empower Labs WebUI")
			.WithEnvironment("WEBUI_SECRET_KEY", webUiSecretKey)
			.WithEnvironment("DOCKER", "true")
			.WithEnvironment("ENABLE_OLLAMA_API", "false")
			.WithEnvironment("ENABLE_AZURE_OPENAI_API", "false")
			.WithEnvironment("ENABLE_OPENAI_API", "true")
			.WithEnvironment("OPENAI_API_BASE_URL", $"{studio.Resource.GetEndpoint("http")}/v1")
			.WithEnvironment("OPENAI_API_KEY", "sk-not-needed")
			// .WithEnvironment("TOOL_SERVER_CONNECTIONS",
			// 	"""
			// 	[
			// 	  {
			// 	    "type": "openapi",
			// 	    "url": "example-url",
			// 	    "spec_type": "url",
			// 	    "spec": "",doc
			// 	    "path": "openapi.json",
			// 	    "auth_type": "none",
			// 	    "key": "",
			// 	    "config": { "enable": true },
			// 	    "info": {
			// 	      "id": "",
			// 	      "name": "example-server",
			// 	      "description": "MCP server description."
			// 	    }
			// 	  }
			// 	]
			// 	""")
			.WithEnvironment("RAG_EMBEDDING_MODEL", embeddingModel)
			.WithEnvironment("RAG_EMBEDDING_ENGINE", "openai")
			// RAG / Vector DB: Qdrant
			.WithEnvironment("ENABLE_RAG", "true")
			.WithEnvironment("VECTOR_DB", "qdrant")
			.WithEnvironment("QDRANT_URI", $"{qdrant.Resource.GetEndpoint("http")}")
			.WithEnvironment("QDRANT_API_KEY", qdrant.Resource.ApiKeyParameter)
			// Web Search: SearXNG
			.WithEnvironment("WEBSEARCH_PROVIDER", "searxng")
			.WithEnvironment("SEARXNG_URL", $"{searXng.Resource.GetEndpoint("http")}")
			// OIDC
			.WithEnvironment("DEFAULT_USER_ROLE", "admin")
			.WithEnvironment("OAUTH_CLIENT_ID", openId.Resource.ClientId)
			.WithEnvironment("OAUTH_CLIENT_SECRET", openId.Resource.ClientSecret)
			.WithEnvironment("OPENID_PROVIDER_URL", openId.Resource.OpenIdConfiguration)
			.WithEnvironment("OAUTH_PROVIDER_NAME", "AI Empower Labs")
			.WithEnvironment("OAUTH_SCOPES", "openid email profile")
			.WithEnvironment("OAUTH_MERGE_ACCOUNTS_BY_EMAIL", "true")
			.WithEnvironment("OAUTH_UPDATE_PICTURE_ON_LOGIN", "true")
			.WithEnvironment("ENABLE_OAUTH_SIGNUP", "true")
			// UI/UX: Open WebUI does not expose official envs to disable welcome/changelog; remove unsupported flags
			.WithEnvironment("ENABLE_VERSION_UPDATE_CHECK", "false")
			.WithCurlHttpHealthCheckEndpoint("http://localhost:8080/health");
	}
}

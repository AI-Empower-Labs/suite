using AppHost.Extensions;
using AppHost.Services;
using AppHost.Setup.Flowise;

namespace AppHost.Setup;

internal static class AiEmpowerLabsStudioRegistration
{
	[ResourceRegistrationOrder(100)]
	public static IResourceBuilder<ContainerResource> Register(IDistributedApplicationBuilder builder)
	{
		return builder
			.AddContainerEx(ResourceNames.Studio, "AEL_STUDIO_IMAGE", "ai-empower-labs/ael-studio", "1.0.1")
			.WithDefaults()
			.WithHttpEndpoint(port: PortAllocationHelper.GetNextAvailablePort(), targetPort: 80)
			.WithEnvironment("ASPNETCORE_URLS", "http://*:80")
			.WithEnvironment("DISABLE_TLS_CERT_VALIDATION", "true")
			.WithUrlForEndpoint("http", static url =>
			{
				url.DisplayText = "AI Inspector";
			});
	}

	public static void Register(
		IDistributedApplicationBuilder builder,
		[Services.ResourceName(ResourceNames.Studio)]
		IResourceBuilder<ContainerResource> studio,
		[Services.ResourceName(ResourceNames.Flowise)]
		IResourceBuilder<FlowiseResource> flowise,
		[Services.ResourceName(ResourceNames.AiEmpowerLabsApiKey)]
		IResourceBuilder<AelLlmApiParameterResource> aiEmpowerLabsApiKey)
	{
		IResourceBuilder<ExternalServiceResource> aiEmpowerLabsBaseUri = builder
			.AddExternalService(ResourceNames.AiEmpowerLabsBaseUri, "https://api.aiempowerlabs.com");

		studio
			.WithEnvironment("FLOWISE_BASE_URL", flowise.Resource.PrimaryEndpoint)
			.WithEnvironment("FLOWISE_API_KEY", flowise.Resource.ApiKeyParameter)
			.WithEnvironment("AEL_BASE_URL", aiEmpowerLabsBaseUri)
			.WithEnvironment("AEL_API_KEY", aiEmpowerLabsApiKey);
	}
}

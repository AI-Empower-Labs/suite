using AppHost.Extensions;
using AppHost.Services;

namespace AppHost.Setup;

internal sealed class AiEmpowerLabsOpenIdResource() : Resource(ResourceNames.OpenId)
{
	public string OpenIdConfiguration => $"{Issuer}/.well-known/openid-configuration";
	public string Issuer { get; init; } = "https://login.aiempowerlabs.com/oidc";
	public string ClientId { get; init; } = "h8z7a4p2bt6mv474nkl3d";
	public string ClientSecret { get; init; } = "fEBiyCbn4ORG7ctKmcpksrrTaYAWQNu1";
}

internal static class OidcRegistration
{
	[ResourceRegistrationOrder(100)]
	public static IResourceBuilder<AiEmpowerLabsOpenIdResource> Register(IDistributedApplicationBuilder builder)
	{
		return builder
			.AddResource(new AiEmpowerLabsOpenIdResource())
			.HideResource();
	}
}

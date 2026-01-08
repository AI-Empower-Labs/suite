using AppHost.Extensions;
using AppHost.Services;

namespace AppHost.Setup;

internal sealed class AiEmpowerLabsOpenIdResource() : Resource(ResourceNames.OpenId)
{
	public string OpenIdConfiguration => $"{Issuer}/.well-known/openid-configuration";
	public string Issuer { get; init; } = "https://login.aiempowerlabs.com/oidc";
	public string ClientId { get; init; } = "h8z7a4p2bt6mv474nkl3d";
	public required ParameterResource ClientSecret { get; init; } 
}

internal static class OidcRegistration
{
	[ResourceRegistrationOrder(100)]
	public static IResourceBuilder<AiEmpowerLabsOpenIdResource> Register(IDistributedApplicationBuilder builder)
	{
		IResourceBuilder<ParameterResource> clientSecret = builder.AddParameter(ResourceNames.OpenIdClientSecret, "fEBiyCbn4ORG7ctKmcpksrrTaYAWQNu1", secret: true);
		
		AiEmpowerLabsOpenIdResource oidc = new()
		{
			ClientSecret = clientSecret.Resource
		};
		
		return builder
			.AddResource(oidc)
			.HideResource();
	}
}

using System.Net.Http.Headers;
using System.Text;

namespace AppHost.Setup.Langfuse;

internal sealed class LangfuseResource : ContainerResource
{
	private const string PrimaryEndpointName = "http";

	public LangfuseResource(string name) : base(name)
	{
		PrimaryEndpoint = new(this, PrimaryEndpointName);
	}

	public EndpointReference PrimaryEndpoint { get; }

	public required string UserName { get; init; }
	public required string Email { get; init; }
	public required ParameterResource Password { get; init; }

    public required ParameterResource PublicKey { get; init; }
    public required ParameterResource SecretKey { get; init; }

    // Expression for "public:secret" that can be resolved later
    public ReferenceExpression AuthenticationTokenExpression =>
        ReferenceExpression.Create($"{PublicKey}:{SecretKey}");

    // Resolves the token and returns Base64("public:secret")
    public async ValueTask<string?> GetAuthenticationTokenBase64(CancellationToken cancellationToken = default)
    {
        string? raw = await AuthenticationTokenExpression.GetValueAsync(cancellationToken);
        if (raw is null)
        {
            return null;
        }

        return Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
    }

    public async ValueTask<AuthenticationHeaderValue> GetAuthenticationHeader(CancellationToken cancellationToken = default)
    {
        string? token = await GetAuthenticationTokenBase64(cancellationToken);
        return new AuthenticationHeaderValue("Basic", token);
    }
}

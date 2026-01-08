namespace AppHost.Setup.Flowise;

internal sealed class FlowiseResource : ContainerResource
{
	private const string PrimaryEndpointName = "http";

	public FlowiseResource(string name) : base(name)
	{
		PrimaryEndpoint = new(this, PrimaryEndpointName);
	}

	public EndpointReference PrimaryEndpoint { get; }

	public required ParameterResource ApiKeyParameter { get; init; }
}

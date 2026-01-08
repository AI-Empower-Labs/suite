namespace AppHost.Setup.Clickhouse;

internal sealed class ClickhouseResource : ContainerResource
{
	private const string PrimaryEndpointName = "http";

	public ClickhouseResource(string name) : base(name)
	{
		PrimaryEndpoint = new(this, PrimaryEndpointName);
	}

	public EndpointReference PrimaryEndpoint { get; }

	public required string Database { get; init; }
	public required string UserName { get; init; }
	public required ParameterResource Password { get; init; }
}

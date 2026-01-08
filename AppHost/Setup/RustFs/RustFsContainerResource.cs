namespace AppHost.Setup.RustFs;

internal sealed class RustFsContainerResource : ContainerResource, IResourceWithConnectionString
{
	internal const string PrimaryEndpointName = "api";

	public RustFsContainerResource(string name) : base(name)
	{
		PrimaryEndpoint = new(this, PrimaryEndpointName);
	}

	/// <summary>
	/// Gets the primary endpoint for the RustFS server.
	/// </summary>
	public EndpointReference PrimaryEndpoint { get; }

	private ReferenceExpression ConnectionString =>
		ReferenceExpression.Create(
			$"{PrimaryEndpoint.Scheme}://{PrimaryEndpoint.Property(EndpointProperty.HostAndPort)}");

	/// <summary>
	/// Gets the connection string expression for the RustFS server.
	/// </summary>
	public ReferenceExpression ConnectionStringExpression
	{
		get
		{
			if (this.TryGetLastAnnotation(out ConnectionStringRedirectAnnotation? connectionStringAnnotation))
			{
				return connectionStringAnnotation.Resource.ConnectionStringExpression;
			}

			return ConnectionString;
		}
	}

	public required ParameterResource AccessKey { get; init; }
	public required ParameterResource SecretKey { get; init; }
	public required string Region { get; init; }

	/// <summary>
	/// Gets the connection string for the RustFS server.
	/// </summary>
	/// <param name="cancellationToken"> A <see cref="CancellationToken"/> to observe while waiting for the task to complete.</param>
	/// <returns>A connection string for the RustFS server in the form "Host=host;Port=port;Username=postgres;Password=password".</returns>
	public ValueTask<string?> GetConnectionStringAsync(CancellationToken cancellationToken = default)
	{
		if (this.TryGetLastAnnotation(out ConnectionStringRedirectAnnotation? connectionStringAnnotation))
		{
			return connectionStringAnnotation.Resource.GetConnectionStringAsync(cancellationToken);
		}

		return ConnectionStringExpression.GetValueAsync(cancellationToken);
	}
}

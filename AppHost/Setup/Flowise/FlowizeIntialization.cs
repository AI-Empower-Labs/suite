using System.Net.Http.Json;

using Dapper;

using Npgsql;

namespace AppHost.Setup.Flowise;

internal static class FlowizeIntialization
{
	public static void Initialize(
		IDistributedApplicationBuilder builder,
		IResourceBuilder<PostgresDatabaseResource> flowiseDatabase,
		TaskCompletionSource<string> flowiseApiKeyTcs,
		IResourceBuilder<FlowiseResource> flowise)
	{
		string? flowiseBaseUrl;
		string? flowiseConnectionString;

#pragma warning disable CS8620 // Argument cannot be used for parameter due to differences in the nullability of reference types.
		builder.OnResourceReady(
			async (_, cancellationToken) =>
			{
				flowiseConnectionString = await flowiseDatabase.Resource.ConnectionStringExpression.GetValueAsync(cancellationToken);
				ResourceUrlAnnotation resourceUrlAnnotation = flowise.Resource.Annotations.OfType<ResourceUrlAnnotation>().Last();
				flowiseBaseUrl = resourceUrlAnnotation.Url;
				await Update(false, cancellationToken);
			}, flowiseDatabase.Resource, flowise.Resource);

		async Task Update(bool apiKeyPollingStarted, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrEmpty(flowiseConnectionString)
				|| string.IsNullOrEmpty(flowiseBaseUrl))
			{
				return;
			}

			string? flowiseApiKey = await GetFlowiseApiKeyFromDatabase(flowiseConnectionString);
			if (string.IsNullOrEmpty(flowiseApiKey))
			{
				// Start a background poller that checks every second until the API key is available,
				// then re-invokes Update to proceed with initialization.
				if (!apiKeyPollingStarted)
				{
					_ = Task.Run(async () =>
					{
						try
						{
							while (true)
							{
								try
								{
									string? key = await GetFlowiseApiKeyFromDatabase(flowiseConnectionString!);
									if (!string.IsNullOrEmpty(key))
									{
										// Found the key, run Update again to continue setup
										await Update(true);
										break;
									}
								}
								catch (Exception e)
								{
									// Swallow and continue polling; optionally log
									Console.WriteLine($"Flowise API key polling error: {e.Message}");
								}

								await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
							}
						}
						finally
						{
							apiKeyPollingStarted = false;
						}
					}, cancellationToken);
				}

				return;
			}

			flowiseApiKeyTcs.TrySetResult(flowiseApiKey);

			await CreateVariable("user-id", flowiseApiKey, cancellationToken);
			await CreateVariable("user-name", flowiseApiKey, cancellationToken);
			await CreateVariable("user-email", flowiseApiKey, cancellationToken);
		}

		async Task CreateVariable(
			string variableName,
			string flowiseApiKey,
			CancellationToken cancellationToken = default)
		{
			if (await Exists("/api/v1/variables/" + variableName, variableName, flowiseApiKey, cancellationToken))
			{
				return;
			}

			using HttpClient httpClient = new();
			using JsonContent content = JsonContent.Create(new
			{
				name = variableName,
				value = "",
				type = "runtime"
			});
			Uri createCredentialsUri = new(new Uri(flowiseBaseUrl!), "/api/v1/variables");
			httpClient.DefaultRequestHeaders.Authorization = new("Bearer", flowiseApiKey);
			using HttpResponseMessage responseMessage = await httpClient.PostAsync(createCredentialsUri, content, cancellationToken);
			if (!responseMessage.IsSuccessStatusCode)
			{
				Console.WriteLine(await responseMessage.Content.ReadAsStringAsync(cancellationToken));
			}
		}

		async Task<bool> Exists(string path, string data, string flowiseApiKey, CancellationToken cancellationToken = default)
		{
			using HttpClient httpClient = new();
			Uri createCredentialsUri = new(new Uri(flowiseBaseUrl!), path);
			httpClient.DefaultRequestHeaders.Authorization = new("Bearer", flowiseApiKey);
			string? content = await httpClient.GetStringAsync(createCredentialsUri, cancellationToken);
			return content?.Contains(data) ?? false;
		}
	}

	private static async Task<string?> GetFlowiseApiKeyFromDatabase(string connectionString)
	{
		try
		{
			await using NpgsqlConnection connection = new(connectionString);
			return await connection
				.QueryFirstOrDefaultAsync<string>("select \"apiKey\" from apikey where \"keyName\"='DefaultKey';");
		}
		catch (Exception e)
		{
			Console.WriteLine(e.Message);
		}

		return null;
	}
}

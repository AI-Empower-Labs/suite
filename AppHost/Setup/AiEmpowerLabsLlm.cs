using AppHost.Services;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AppHost.Setup;

internal static class AiEmpowerLabsLlm
{
	[ResourceRegistrationOrder(100)]
	public static IResourceBuilder<AelLlmApiParameterResource> Register(IDistributedApplicationBuilder builder)
	{
		builder
			.AddParameter(ResourceNames.EmbeddingModel, "BAAI/bge-m3", true);

		AelLlmApiParameterResource aelLlmApiParameterResource = new(ResourceNames.AiEmpowerLabsApiKey, builder.Configuration);
		IResourceBuilder<AelLlmApiParameterResource> apiKeyResourceBuilder = builder
			.AddResource(aelLlmApiParameterResource);
		apiKeyResourceBuilder
			.WithDescription("AI Empower Labs API Key")
			.WithCustomInput(p => new()
			{
				InputType = InputType.Text,
				Value = "sk-",
				Name = p.Name,
				Placeholder = $"Enter value for {p.Name}",
				Description = p.Description,
				Required = true
			})
			.OnResourceReady(async (_, @event, cancellationToken) =>
			{
				IInteractionService interactionService = @event.Services.GetRequiredService<IInteractionService>();
				await interactionService.PromptNotificationAsync(
					title: "Information",
					message: "Visit [AI Empower Labs](https://www.aiempowerlabs.com) for more information.",
					options: new NotificationInteractionOptions
					{
						Intent = MessageIntent.Information,
						EnableMessageMarkdown = true
					}, cancellationToken: cancellationToken);
			});

		return apiKeyResourceBuilder;
	}
}

internal sealed class AelLlmApiParameterResource(string name, IConfiguration configuration) : ParameterResource(
	name,
	parameterDefault =>
	{
		string configurationKey = $"Parameters:{name}";
		string? value = configuration[configurationKey];
		// If not found, try with underscores as a fallback
		if (string.IsNullOrEmpty(value))
		{
			string normalizedKey = configurationKey.Replace("-", "_", StringComparison.Ordinal);
			value = configuration[normalizedKey];
		}

		return value
			?? parameterDefault?.GetDefaultValue()
			?? throw new MissingParameterValueException($"Parameter resource could not be used because configuration key '{configurationKey}' is missing and the Parameter has no default value.");
	},
	true);

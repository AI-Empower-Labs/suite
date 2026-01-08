using System.Reflection;

using Microsoft.Extensions.DependencyInjection;

namespace AppHost.Services;

/// <summary>
/// Specifies the name of the resource to be injected into a registration method.
/// </summary>
/// <param name="name">The name of the resource.</param>
[AttributeUsage(AttributeTargets.Parameter)]
internal sealed class ResourceNameAttribute(string name) : Attribute
{
	public string Name => name;
}

[AttributeUsage(AttributeTargets.Method)]
internal sealed class ResourceRegistrationOrderAttribute(int rank) : Attribute
{
	public int Rank => rank;
}

/// <summary>
/// Automatically registers resources for the Aspire application host by discovering and invoking "Register" methods.
/// </summary>
internal static class AspireResourceAutoRegistrar
{
	/// <summary>
	/// Scans the entry assembly for static "Register" methods and invokes them in dependency order to configure the application builder.
	/// </summary>
	/// <param name="builder">The distributed application builder.</param>
	public static void AutoRegister(this IDistributedApplicationBuilder builder)
	{
		MethodInfo[] factories = Assembly.GetEntryAssembly()!
			.GetTypes()
			.SelectMany(static type => type.GetMethods(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static)
				.Where(info => info.Name.Equals("Register", StringComparison.Ordinal)))
			.OrderBy(methodInfo =>
			{
				ResourceRegistrationOrderAttribute? orderAttribute = methodInfo
					.GetCustomAttributes<ResourceRegistrationOrderAttribute>()
					.FirstOrDefault();
				return orderAttribute?.Rank ?? int.MaxValue;
			})
			.ThenBy(methodInfo => methodInfo?.GetParameters().Length ?? 0)
			.ToArray()!;

		ServiceCollection collection = [];
		collection.AddSingleton(builder);
		List<IResourceBuilder<IResource>> resourceBuilders = [];
		foreach (MethodInfo methodInfo in factories)
		{
			ParameterInfo[] parameterInfos = methodInfo.GetParameters();
			object?[] parameters = parameterInfos
				.Select(object? (parameter) => GetService(builder, parameter, resourceBuilders))
				.ToArray();

			if (methodInfo.ReturnType == typeof(void))
			{
				methodInfo.Invoke(null, parameters);
			}
			else
			{
				object? result = methodInfo.Invoke(null, parameters);
				if (result is IResourceBuilder<IResource> resourceBuilder)
				{
					resourceBuilders.Add(resourceBuilder);
				}
			}
		}
	}

	/// <summary>
	/// Resolves a service or resource for a parameter in a registration method.
	/// </summary>
	/// <param name="builder">The distributed application builder.</param>
	/// <param name="parameter">The parameter info to resolve.</param>
	/// <param name="resourceBuilders">The list of already created resource builders.</param>
	/// <returns>The resolved service object, or null if allowed.</returns>
	/// <exception cref="Exception">Thrown if the resource name attribute is missing or if a required resource is not found.</exception>
	private static object? GetService(IDistributedApplicationBuilder builder,
		ParameterInfo parameter,
		List<IResourceBuilder<IResource>> resourceBuilders)
	{
		if (parameter.ParameterType == typeof(IDistributedApplicationBuilder))
		{
			return builder;
		}

		ResourceNameAttribute resourceNameAttribute = parameter
			.GetCustomAttribute<ResourceNameAttribute>() ?? throw new Exception("No resource name attribute");
		foreach (IResource resource in builder.Resources)
		{
			if (resource.Name.Equals(resourceNameAttribute.Name, StringComparison.Ordinal)
				&& parameter.ParameterType.IsInstanceOfType(resource))
			{
				return resource;
			}
		}

		foreach (IResourceBuilder<IResource> resourceBuilder in resourceBuilders)
		{
			if (resourceBuilder.Resource.Name.Equals(resourceNameAttribute.Name, StringComparison.Ordinal)
				&& parameter.ParameterType.IsInstanceOfType(resourceBuilder))
			{
				return resourceBuilder;
			}
		}

		Type parameterType = parameter.ParameterType;
		bool isNullable = !parameterType.IsValueType // reference type
			|| Nullable.GetUnderlyingType(parameterType) is not null; // Nullable<T>
		if (isNullable)
		{
			return null;
		}

		throw new Exception("No resource found");
	}
}

using System.Net;

namespace AppHost.Services;

internal static class PortAllocationHelper
{
	private const int MaxPort = 65000;
	private static int s_currentPort = 3000;

	public static int GetNextAvailablePort()
	{

		int candidate = s_currentPort;
		while (candidate <= MaxPort && !IsPortAvailable(candidate))
		{
			candidate++;
		}

		if (candidate > MaxPort)
		{
			throw new InvalidOperationException("No available ports remain in allocated range.");
		}

		s_currentPort = candidate + 100;
		return candidate;
	}

	private static bool IsPortAvailable(int port)
	{
		// Check if port is in use by examining active TCP listeners
		// This avoids binding the port which would create a race condition
		System.Net.NetworkInformation.IPGlobalProperties ipGlobalProperties = System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties();
		IPEndPoint[] listeners = ipGlobalProperties.GetActiveTcpListeners();
		return listeners.All(endpoint => endpoint.Port != port);
	}
}

using AppHost.Services;

IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(
	new DistributedApplicationOptions
	{
		Args = args,
		DashboardApplicationName = "AI Empower Labs Suite",
		AllowUnsecuredTransport = true
	});
builder.AutoRegister();
builder.Build().Run();

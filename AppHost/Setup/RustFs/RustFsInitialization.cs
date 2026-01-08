using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AppHost.Setup.RustFs;

internal static class RustFsInitialization
{
	public static string RustFsRegion(IConfiguration configuration)
	{
		string region = configuration["RUSTFS_REGION"] ?? "us-east-1";
		return region;
	}

	public static async Task Initialize(
		ILogger logger,
		RustFsContainerResource rustFsContainerResource,
		string rustFsEndpoint,
		string[] buckets,
		CancellationToken cancellationToken)
	{
		if (string.IsNullOrEmpty(rustFsEndpoint)
			|| buckets.Length == 0)
		{
			return;
		}

		using HttpClient http = new();
		// Wait up to ~2 minutes for RustFS to report healthy
		for (int i = 0; i < 60; i++)
		{
			try
			{
				HttpResponseMessage resp = await http.GetAsync($"{rustFsEndpoint}/health", cancellationToken);
				if (resp.IsSuccessStatusCode)
					break;
			}
			catch
			{
				// ignore transient errors while RustFS starts
			}

			await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
		}

		AmazonS3Config s3Config = new()
		{
			ServiceURL = rustFsEndpoint,
			ForcePathStyle = true,
			AuthenticationRegion = rustFsContainerResource.Region
		};
		using AmazonS3Client s3 = new(
			await rustFsContainerResource.AccessKey.GetValueAsync(cancellationToken),
			await rustFsContainerResource.SecretKey.GetValueAsync(cancellationToken),
			s3Config);

		foreach (string bucket in buckets)
		{
			try
			{
				bool exists = await AmazonS3Util.DoesS3BucketExistV2Async(s3, bucket);
				if (!exists)
				{
					await s3.PutBucketAsync(new PutBucketRequest
					{
						BucketName = bucket,
						UseClientRegion = true
					}, cancellationToken);
					logger.LogInformation($"[rustfs-init] Created bucket '{bucket}'.");
				}
				else
				{
					logger.LogInformation($"[rustfs-init] Bucket '{bucket}' already exists.");
				}
			}
			catch (Exception ex)
			{
				logger.LogInformation($"[rustfs-init] Bucket '{bucket}' check/create failed: {ex.Message}");
			}
		}

		logger.LogInformation("[rustfs-init] Done.");
	}
}

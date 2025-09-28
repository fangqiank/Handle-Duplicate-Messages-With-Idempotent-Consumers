namespace Handle_Duplicate_Messages_With_Idempotent_Consumers.Services
{
    public class AwsConfiguration
    {
        public const string SectionName = "Aws";

        public string Region { get; set; } = "us-east-1";
        public string AccessKey { get; set; } = string.Empty;
        public string SecretKey { get; set; } = string.Empty;
        public string QueueName { get; set; } = "order-processing-queue";
        public int MaxRetries { get; set; } = 3;
        public int VisibilityTimeoutSeconds { get; set; } = 30;
        public bool UseLocalStack { get; set; } = false;
        public string LocalStackUrl { get; set; } = "http://localhost:4566";
    }

    public static class AwsServiceCollectionExtensions
    {
        public static IServiceCollection AddAwsServices(this IServiceCollection services, IConfiguration configuration)
        {
            var awsConfig = configuration.GetSection(AwsConfiguration.SectionName).Get<AwsConfiguration>()
                ?? new AwsConfiguration();

            services.Configure<AwsConfiguration>(configuration.GetSection(AwsConfiguration.SectionName));

            // Configure AWS options
            var options = new AmazonSQSConfig
            {
                RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(awsConfig.Region)
            };

            if (awsConfig.UseLocalStack)
            {
                options.ServiceURL = awsConfig.LocalStackUrl;
                options.AuthenticationRegion = awsConfig.Region;
                options.UseHttp = true;
            }

            // Register SQS client
            services.AddAWSService<IAmazonSQS>(options);
            services.AddScoped<SqsQueueService>();
            services.AddScoped<SqsMessageProcessor>();

            return services;
        }
    }
}
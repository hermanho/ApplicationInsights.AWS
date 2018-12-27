using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;


namespace ApplicationInsights.AWS
{
    public class AWSStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        {
            return builder =>
            {
                var customizer = builder.ApplicationServices.GetRequiredService<ApplicationInsightsPipelineCustomizer>();
                Amazon.Runtime.Internal.RuntimePipelineCustomizerRegistry.Instance.Register(customizer);
                next(builder);
            };
        }
    }

    public static class AWSInjection
    {
        public static IServiceCollection AddAWSInjection(this IServiceCollection services)
        {
            services.AddTransient<IStartupFilter, AWSStartupFilter>();
            services.AddSingleton<ApplicationInsightsPipelineCustomizer>();
            services.AddTransient<ApplicationInsightsPipelineHandler>();
            services.AddTransient<ApplicationInsightsExceptionsPipelineHandler>();
            services.Configure<ApplicationInsightsPipelineOption>(option =>
            {
                option.RegisterAll = true;
            });
            return services;
        }

        public static IWebHostBuilder UseApplicationInsightsAWSInjection(this IWebHostBuilder builder)
        {
            return builder.ConfigureServices((IServiceCollection services) =>
            {
                services.AddAWSInjection();
            });
        }
    }
}

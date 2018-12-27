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
                var environment = builder.ApplicationServices.GetRequiredService<IHostingEnvironment>();
                var customizer = builder.ApplicationServices.GetRequiredService<ApplicationInsightsPipelineCustomizer>();
                var options = builder.ApplicationServices.GetRequiredService<IOptions<ApplicationInsightsPipelineOption>>();
                Amazon.Runtime.Internal.RuntimePipelineCustomizerRegistry.Instance.Register(customizer);
                next(builder);
            };
        }
    }

    public static class AWSInjection
    {
        public static IWebHostBuilder UseApplicationInsightsAWSInjection(this IWebHostBuilder builder)
        {
            return builder.ConfigureServices((IServiceCollection services) =>
            {
                services.AddTransient<IStartupFilter, AWSStartupFilter>();
                services.AddSingleton<ApplicationInsightsPipelineCustomizer>();
                services.AddSingleton<ApplicationInsightsPipelineHandler>();
                services.AddSingleton<ApplicationInsightsExceptionsPipelineHandler>();
                services.Configure<ApplicationInsightsPipelineOption>(option =>
                {
                    option.RegisterAll = true;
                });
            });
        }
    }
}

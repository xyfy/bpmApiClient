using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using BpmApiClient;
using BpmApiClient.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BpmApiHost
{
    /// <summary>
    /// ASP.NET Core 2.2 应用程序启动配置。
    /// 注册 BPM API 客户端、MVC 服务及基础中间件。
    /// </summary>
    public class Startup
    {
        /// <summary>应用配置（来自 appsettings.json）。</summary>
        private readonly IConfiguration _configuration;

        /// <summary>初始化 Startup，注入配置对象。</summary>
        public Startup(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        /// <summary>
        /// 配置依赖注入容器。
        /// 此方法由运行时调用，用于向容器添加服务。
        /// </summary>
        public void ConfigureServices(IServiceCollection services)
        {
            // 读取 BPM 客户端配置（来自 appsettings.json 中的 BpmApiClient 节点）
            var bpmOptions = new BpmApiClientOptions();
            _configuration.GetSection("BpmApiClient").Bind(bpmOptions);

            // 注册 HttpClient（单例），并绑定 BPM 服务基础地址
            services.AddSingleton<HttpClient>(sp =>
            {
                var client = new HttpClient
                {
                    BaseAddress = new Uri(bpmOptions.BaseUrl.TrimEnd('/') + "/", UriKind.Absolute),
                    Timeout = bpmOptions.Timeout
                };
                return client;
            });

            // 注册 BpmApiClientOptions 配置
            services.AddSingleton(bpmOptions);

            // 注册 IBpmApiClient 的 HTTP 实现（单例，内含令牌缓存，线程安全）
            services.AddSingleton<IBpmApiClient, BpmApiClientImpl>();

            // 注册 MVC（兼容 ASP.NET Core 2.2）
            services.AddMvc().SetCompatibilityVersion(CompatibilityVersion.Version_2_2);
        }

        /// <summary>
        /// 配置 HTTP 请求处理管道中间件。
        /// 此方法由运行时调用，用于配置中间件流水线。
        /// </summary>
        public void Configure(IApplicationBuilder app, IHostingEnvironment env)
        {
            if (env.IsDevelopment())
            {
                // 开发环境：显示详细异常页
                app.UseDeveloperExceptionPage();
            }
            else
            {
                // 生产环境：统一异常处理，返回 500 错误
                app.UseExceptionHandler(errorApp =>
                {
                    errorApp.Run(async context =>
                    {
                        context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                        context.Response.ContentType = "text/plain; charset=utf-8";
                        await context.Response.WriteAsync("服务器内部错误，请联系管理员。").ConfigureAwait(false);
                    });
                });
            }

            // 强制 HTTPS 重定向
            app.UseHttpsRedirection();

            // 启用 MVC 路由
            app.UseMvc();
        }
    }
}

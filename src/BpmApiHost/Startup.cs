using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using BpmApiClient;
using BpmApiClient.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
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

            // 提前校验必填配置项，启动时即给出清晰错误信息（早于 BpmApiClientImpl 构造函数的校验）
            if (string.IsNullOrWhiteSpace(bpmOptions.BaseUrl))
                throw new InvalidOperationException(
                    "配置项 BpmApiClient:BaseUrl 不能为空，请在 appsettings.json 中填写 BPM 服务地址。");
            if (string.IsNullOrWhiteSpace(bpmOptions.AppId))
                throw new InvalidOperationException(
                    "配置项 BpmApiClient:AppId 不能为空，请在 appsettings.json 中填写应用 ID。");
            if (string.IsNullOrWhiteSpace(bpmOptions.Secret))
                throw new InvalidOperationException(
                    "配置项 BpmApiClient:Secret 不能为空，请在 appsettings.json 中填写应用密钥。");

            // 注册命名 HttpClient，通过 IHttpClientFactory 管理 HttpMessageHandler 生命周期
            // （防止长寿命 HttpClient 导致的 DNS 过期问题）
            services.AddHttpClient("bpm", client =>
            {
                client.BaseAddress = new Uri(bpmOptions.BaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
                client.Timeout = bpmOptions.Timeout;
            });

            // 注册 BpmApiClientOptions 配置
            services.AddSingleton(bpmOptions);

            // 注册 IBpmApiClient 的 HTTP 实现（单例，内含令牌缓存，线程安全）。
            // 通过 Func<HttpClient> 委托将 IHttpClientFactory.CreateClient("bpm") 传入，
            // 每次 HTTP 调用都会从工厂获取 HttpClient，确保 handler 随框架生命周期轮换。
            services.AddSingleton<IBpmApiClient>(sp =>
            {
                var factory = sp.GetRequiredService<IHttpClientFactory>();
                return new BpmApiClientImpl(() => factory.CreateClient("bpm"), bpmOptions);
            });

            // 注册 MVC（兼容 ASP.NET Core 2.2）
            services.AddMvc().SetCompatibilityVersion(CompatibilityVersion.Version_2_2);

            // 注册 JWT Bearer 认证（运维接口受 [Authorize] 保护，需配合认证中间件）
            // 在 appsettings.json 中配置 Auth:Authority 和 Auth:Audience
            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(options =>
                {
                    options.Authority = _configuration["Auth:Authority"];
                    options.Audience = _configuration["Auth:Audience"];
                });

            // 注册授权服务（运维接口受保护，部署时应配合认证中间件使用）
            services.AddAuthorization();
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

            // HTTPS 重定向仅在非开发环境启用（开发环境仅配置了 http，重定向会导致接口不可访问）
            if (!env.IsDevelopment())
            {
                app.UseHttpsRedirection();
            }

            // 启用认证中间件（在 MVC 之前，使 [Authorize] 能正确验证 JWT 令牌）
            app.UseAuthentication();

            // 启用 MVC 路由
            app.UseMvc();
        }
    }
}

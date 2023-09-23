using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi.Models;
using SyncStreamAPI.DataContext;
using SyncStreamAPI.Games.Blackjack;
using SyncStreamAPI.Games.Gallows;
using SyncStreamAPI.Helper;
using SyncStreamAPI.Hubs;
using SyncStreamAPI.Interfaces;
using SyncStreamAPI.ServerData;
using SyncStreamAPI.ServerData.Background;

namespace SyncStreamAPI
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddSingleton<IContentTypeProvider, FileExtensionContentTypeProvider>();
            services.AddHostedService<DataBackgroundService>();
            services.AddHostedService<ServerHealthBackgroundService>();
            services.AddSingleton(provider =>
            {
                MainManager manager = new MainManager(provider);
                return manager;
            });
            /*services.AddSingleton(provider =>
            {
                WebRtcSfuManager webRtcSfuManager = new WebRtcSfuManager(Configuration, provider);
                return webRtcSfuManager;
            });*/
            services.AddDbContext<PostgresContext>(options =>
            {
                options.UseNpgsql(Configuration.GetConnectionString("SyncStreamDB"));
            });
#if DEBUG
            var origins = new string[8] { "https://dreckbu.de", "https://*.dreckbu.de", "https://dreckbu.de/*", "https://drecktu.be", "https://*.drecktu.be", "https://drecktu.be/*", "http://localhost:4200", "https://localhost:4200" };
#else
            var origins = new string[6] { "https://dreckbu.de", "https://*.dreckbu.de", "https://dreckbu.de/*","https://drecktu.be", "https://*.drecktu.be", "https://drecktu.be/*" };
            
#endif
            services.AddCors(o => o.AddPolicy("CORSPolicy", builder =>
            {
                builder.SetIsOriginAllowedToAllowWildcardSubdomains()
                        .SetIsOriginAllowed(hostname => true)
                        .WithOrigins(origins)
                       .AllowAnyMethod()
                       .AllowAnyHeader()
                       .AllowCredentials();
            }));
            services.AddSignalR(options =>
            {
                options.EnableDetailedErrors = false;
            }).AddNewtonsoftJsonProtocol();
            services.AddSingleton(provider =>
            {
                BrowserAutomation browser = new BrowserAutomation(provider);
                BrowserAutomation.Init();
                return browser;
            });
            services.AddSingleton(provider =>
            {
                var hubContext = provider.GetService<IHubContext<ServerHub, IServerHub>>();
                GallowGameManager manager = new GallowGameManager(hubContext);
                return manager;
            });
            services.AddSingleton(provider =>
            {
                var hubContext = provider.GetService<IHubContext<ServerHub, IServerHub>>();
                BlackjackManager manager = new BlackjackManager(hubContext);
                return manager;
            });
            services.AddControllers();
            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo { Title = "Drecktube API", Version = "v1" });
            });
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, MainManager manager, IHostApplicationLifetime applicationLifetime, BrowserAutomation? browser) //, WebRtcSfuManager webRtcSfuManager
        {
            var forwardingOptions = new ForwardedHeadersOptions() { ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.All };
            app.UseForwardedHeaders(forwardingOptions);
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                app.UseSwagger();
                app.UseSwaggerUI(c =>
                {
                    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Drecktube API");
                });
            }
            app.UseHttpsRedirection();
            app.UseRouting();
            app.UseCors("CORSPolicy");
            app.UseAuthentication();
            app.UseAuthorization();
            applicationLifetime.ApplicationStopping.Register(() =>
            {
                BrowserAutomation.Browser?.Dispose();
            });

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
                endpoints.MapHub<ServerHub>("/server");
            });
        }
    }
}

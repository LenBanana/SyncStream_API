using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SyncStreamAPI.DataContext;
using SyncStreamAPI.Games.Blackjack;
using SyncStreamAPI.Games.Gallows;
using SyncStreamAPI.Hubs;
using SyncStreamAPI.Interfaces;
using SyncStreamAPI.ServerData;

namespace SyncStreamAPI
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
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
            //
            }));
            services.AddSignalR(options => {
                options.EnableDetailedErrors = false;
            }).AddNewtonsoftJsonProtocol();
            services.AddSingleton(provider =>
            {
                var hubContext = provider.GetService<IHubContext<ServerHub, IServerHub>>();
                DataManager manager = new DataManager(hubContext);
                return manager;
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
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, DataManager manager, PostgresContext postgres)
        {
            try
            {
                postgres.Database.EnsureCreated();
            } catch { }
            var forwardingOptions = new ForwardedHeadersOptions() { ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.All };
            app.UseForwardedHeaders(forwardingOptions);
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            app.UseHttpsRedirection();
            app.UseRouting();
            app.UseCors("CORSPolicy");
            app.UseAuthentication();
            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
                endpoints.MapHub<ServerHub>("/server");
            });
        }
    }
}

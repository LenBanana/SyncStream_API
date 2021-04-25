using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SyncStreamAPI.DataContext;
using SyncStreamAPI.Hubs;
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
            services.AddDbContext<MariaContext>(options =>
            {
                options.UseMySql(Configuration.GetConnectionString("SyncStreamDB"));
            });
            services.AddCors(o => o.AddPolicy("MyPolicy", builder =>
            {
                builder.SetIsOriginAllowedToAllowWildcardSubdomains()
                        .SetIsOriginAllowed(hostname => true)
                        .WithOrigins(new string[] { "http://localhost:4200", "https://localhost:4200", "https://dreckbu.de", "https://*.dreckbu.de", "https://dreckbu.de/*" })
                       .AllowAnyMethod()
                       .AllowAnyHeader()
                       .AllowCredentials();
            //
            }));
            services.AddSignalR(options => {
                options.EnableDetailedErrors = false;
            });
            services.AddSingleton(provider =>
            {
                var hubContext = provider.GetService<IHubContext<ServerHub>>();
                DataManager manager = new DataManager(hubContext);
                return manager;
            });
            services.AddControllers();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, DataManager manager)
        {
            var forwardingOptions = new ForwardedHeadersOptions() { ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.All }; app.UseForwardedHeaders(forwardingOptions);
            app.UseForwardedHeaders(forwardingOptions);
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            app.UseHttpsRedirection();
            app.UseRouting();
            app.UseCors("MyPolicy");
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

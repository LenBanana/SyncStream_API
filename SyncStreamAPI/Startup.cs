using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Server.IIS;
using Microsoft.AspNetCore.Server.Kestrel.Core;
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
using SyncStreamAPI.Helper.SponsorBlock;
using SyncStreamAPI.Helper.Streaming;
using SyncStreamAPI.Hubs;
using SyncStreamAPI.Interfaces;
using SyncStreamAPI.ServerData;
using SyncStreamAPI.ServerData.Background;
using SyncStreamAPI.Auth;

namespace SyncStreamAPI;

public class Startup
{
    public Startup(IConfiguration configuration)
    {
        Configuration = configuration;
    }

    public IConfiguration Configuration { get; }

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddHttpClient();
        // Uploads arrive in chunks, so a single request never needs to be huge. These are bounds
        // against an unauthenticated disk fill, not against the upload feature.
        services.Configure<KestrelServerOptions>(options =>
        {
            options.Limits.MaxRequestBodySize = General.MaxRequestBodyBytes;
        });
        services.Configure<IISServerOptions>(options =>
        {
            options.MaxRequestBodySize = General.MaxRequestBodyBytes;
        });
        services.Configure<FormOptions>(options =>
        {
            options.MultipartBodyLengthLimit = General.MaxRequestBodyBytes;
            options.ValueLengthLimit = General.MaxFormValueBytes;
            options.MultipartHeadersLengthLimit = General.MaxMultipartHeaderBytes;
        });

        services.AddSingleton<IContentTypeProvider, FileExtensionContentTypeProvider>();
        services.AddSingleton<ISponsorBlockService, SponsorBlockService>();
        services.AddScoped<IRoomStreamService, RoomStreamService>();
        services.AddHostedService<DataBackgroundService>();
        services.AddHostedService<ServerHealthBackgroundService>();
        services.AddSingleton(provider =>
        {
            var manager = new MainManager(provider);
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
        var origins = new string[8]
        {
            "https://dreckbu.de", "https://*.dreckbu.de", "https://dreckbu.de/*", "https://drecktu.be",
            "https://*.drecktu.be", "https://drecktu.be/*", "http://localhost:4200", "https://localhost:4200"
        };
#else
            var origins =
 new string[6] { "https://dreckbu.de", "https://*.dreckbu.de", "https://dreckbu.de/*","https://drecktu.be", "https://*.drecktu.be", "https://drecktu.be/*" };

#endif
        services.AddCors(o => o.AddPolicy("CORSPolicy", builder =>
        {
            builder.SetIsOriginAllowedToAllowWildcardSubdomains()
                .WithOrigins(origins)
                .AllowAnyMethod()
                .AllowAnyHeader()
                .AllowCredentials();
        }));
        services.AddHttpContextAccessor();

        // One place turns a credential into an identity. Anonymous connections are allowed through so
        // guests can watch; every gated action goes through a policy instead.
        services.AddAuthentication(TokenAuthDefaults.Scheme)
            .AddScheme<TokenAuthOptions, RememberTokenAuthenticationHandler>(TokenAuthDefaults.Scheme, null)
            .AddScheme<ApiKeyAuthOptions, ApiKeyAuthenticationHandler>(ApiKeyAuthenticationHandler.Scheme, null);
        services.AddAuthorization(options => options.AddSyncStreamPolicies());
        services.AddSingleton<IUserIdProvider, DbUserIdProvider>();

        services.AddSignalR(options => { options.EnableDetailedErrors = false; }).AddNewtonsoftJsonProtocol();
        services.AddSingleton(provider =>
        {
            var browser = new BrowserAutomation(provider);
            BrowserAutomation.Init();
            return browser;
        });
        services.AddSingleton(provider =>
        {
            var hubContext = provider.GetService<IHubContext<ServerHub, IServerHub>>();
            var manager = new GallowGameManager(hubContext);
            return manager;
        });
        services.AddSingleton(provider =>
        {
            var hubContext = provider.GetService<IHubContext<ServerHub, IServerHub>>();
            var manager = new BlackjackManager(hubContext);
            return manager;
        });
        services.AddSingleton<WebRtcSfuManager>();
        services.AddSingleton<RtmpFileShareManager>();
        services.AddControllers();
        services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo { Title = "Drecktube API", Version = "v1" });
        });
    }

    public void Configure(IApplicationBuilder app, IWebHostEnvironment env, MainManager manager,
        IHostApplicationLifetime applicationLifetime, BrowserAutomation? browser) //, WebRtcSfuManager webRtcSfuManager
    {
        var forwardingOptions = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.All
        };
        app.UseForwardedHeaders(forwardingOptions);
        if (env.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
            app.UseSwagger();
            app.UseSwaggerUI(c => { c.SwaggerEndpoint("/swagger/v1/swagger.json", "Drecktube API"); });
        }

        app.UseHttpsRedirection();
        app.UseRouting();
        app.UseCors("CORSPolicy");
        app.UseAuthentication();
        app.UseAuthorization();
        applicationLifetime.ApplicationStopping.Register(() => { BrowserAutomation.Browser?.Dispose(); });

        app.UseEndpoints(endpoints =>
        {
            endpoints.MapControllers();
            endpoints.MapHub<ServerHub>("/server");
        });
    }
}
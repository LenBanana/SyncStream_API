using AspectInjector.Broker;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SyncStreamAPI.DataContext;
using SyncStreamAPI.Enums;
using SyncStreamAPI.Hubs;
using SyncStreamAPI.Interfaces;
using SyncStreamAPI.Models;
using SyncStreamAPI.PostgresModels;
using SyncStreamAPI.ServerData;
using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace SyncStreamAPI.Annotations
{
    [Aspect(Scope.Global)]
    [Injection(typeof(PrivilegeAttribute))]
    public class PrivilegeAttribute : Attribute
    {
        public UserPrivileges RequiredPrivileges { get; set; }
        public AuthenticationType AuthenticationType { get; set; }
        public int TokenPosition { get; set; } = 0;

        public PrivilegeAttribute()
        {
        }

        [Advice(Kind.Around, Targets = Target.Method)]
        public object PrivilegeEnter(
            [Argument(Source.Metadata)] MethodBase method,
            [Argument(Source.Name)] string name,
            [Argument(Source.Arguments)] object[] args,
            [Argument(Source.Target)] Func<object[], object> target)
        {
            try
            {
                var attribute = (PrivilegeAttribute)method.GetCustomAttribute(typeof(PrivilegeAttribute));
                if (attribute != null && (args == null || args.Length <= attribute.TokenPosition ||
                                          args[attribute.TokenPosition] is not string))
                {
                    Console.WriteLine("First argument has to be of type 'string'");
                    return Task.FromResult(false);
                }

                if (attribute != null)
                {
                    var firstArg = args[attribute.TokenPosition];
                    return Task.FromResult(HasPrivileges(attribute, (string)firstArg).Result ? target(args) : false);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in '{name}'");
                Console.WriteLine(ex.ToString());
            }
            return Task.FromResult(false);
        }

        private async Task<bool> HasPrivileges(PrivilegeAttribute attribute, string authKey)
        {
            using var scope = MainManager.ServiceProvider.CreateScope();
            var hub = scope.ServiceProvider.GetRequiredService<IHubContext<ServerHub, IServerHub>>();
            var postgres = scope.ServiceProvider.GetRequiredService<PostgresContext>();
            var dbUser = postgres.Users?.Include(x => x.RememberTokens).SingleOrDefault(u =>
                (attribute.AuthenticationType == AuthenticationType.API && u.ApiKey == authKey)
                || (attribute.AuthenticationType == AuthenticationType.Token && u.RememberTokens != null &&
                    u.RememberTokens.Any(y => y.Token == authKey)));
            if (dbUser == null)
                return false;
            if (dbUser.userprivileges >= attribute.RequiredPrivileges) return true;
            await hub.Clients.Group(dbUser.ID.ToString()).dialog(new Dialog(AlertType.Danger)
            {
                Question = "You do not have permissions to perform this action", Answer1 = "Ok",
                Header = "Permission denied"
            });
            return false;
        }
    }
}
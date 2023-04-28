﻿using AspectInjector.Broker;
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
        public PrivilegeAttribute() { }

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
                if (args == null || args.Length == 0 || args[0] is not string)
                {
                    Console.WriteLine("First argument has to be of type 'string'");
                    return new StatusCodeResult(StatusCodes.Status400BadRequest);
                }
                var firstArg = args[0];
                if (HasPrivileges(attribute, (string)firstArg).Result)
                {
                    var result = target(args);
                    return result;
                }
                else
                {
                    return new StatusCodeResult(StatusCodes.Status401Unauthorized);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in '{name}'");
                Console.WriteLine(ex.ToString());
                throw;
            }
        }

        private async Task<bool> HasPrivileges(PrivilegeAttribute attribute, string authKey)
        {
            using (var scope = MainManager.ServiceProvider.CreateScope())
            {
                var hub = scope.ServiceProvider.GetRequiredService<IHubContext<ServerHub, IServerHub>>();
                var postgres = scope.ServiceProvider.GetRequiredService<PostgresContext>();
                var dbUser = postgres.Users?.Include(x => x.RememberTokens).SingleOrDefault(u =>
                (attribute.AuthenticationType == AuthenticationType.API && u.ApiKey == authKey)
                || (attribute.AuthenticationType == AuthenticationType.Token && u.RememberTokens != null && u.RememberTokens.Any(y => y.Token == authKey)));
                if (dbUser == null)
                    return false;
                if (dbUser.userprivileges < attribute.RequiredPrivileges)
                {
                    await hub.Clients.Group(dbUser.ID.ToString()).dialog(new Dialog(AlertType.Danger) { Question = "You do not have permissions to perform this action", Answer1 = "Ok", Header = "Permission denied" });
                    return false;
                }
            }
            return true;
        }
    }
}
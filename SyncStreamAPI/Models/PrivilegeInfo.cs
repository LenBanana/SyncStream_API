using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;
using Microsoft.AspNetCore.Authorization;
using SyncStreamAPI.Auth;
using SyncStreamAPI.Helper;
using SyncStreamAPI.PostgresModels;

namespace SyncStreamAPI.Models;

public class PrivilegeInfo
{
    /// <summary>
    ///     Maps a policy name back to the privilege level it requires, so the admin view can keep showing a
    ///     level rather than a policy string.
    /// </summary>
    private static readonly Dictionary<string, UserPrivileges> PolicyPrivileges = new()
    {
        [AuthorizationPolicies.Approved] = UserPrivileges.Approved,
        [AuthorizationPolicies.Moderator] = UserPrivileges.Moderator,
        [AuthorizationPolicies.Administrator] = UserPrivileges.Administrator,
        [AuthorizationPolicies.Elevated] = UserPrivileges.Elevated
    };

    private PrivilegeInfo(string methodName, string typeName, string description, UserPrivileges requiredPrivileges,
        string policy)
    {
        MethodName = methodName;
        TypeName = typeName;
        Description = description;
        RequiredPrivileges = requiredPrivileges;
        Policy = policy;
    }

    public string MethodName { get; set; }
    public string TypeName { get; set; }
    public string Description { get; set; }
    public UserPrivileges RequiredPrivileges { get; set; }

    /// <summary>The authorization policy guarding the method.</summary>
    public string Policy { get; set; }

    /// <summary>
    ///     Lists every method guarded by an authorization policy, for the admin permissions view.
    /// </summary>
    /// <remarks>
    ///     Reads <see cref="AuthorizeAttribute" /> rather than the old <c>[Privilege]</c> aspect. A method
    ///     guarded by a policy this does not recognise is still listed, with an unknown level, so a new policy
    ///     shows up as unlabelled rather than vanishing from the view.
    /// </remarks>
    public static List<PrivilegeInfo> GetPrivilegedMethodsInfo()
    {
        var assembly = typeof(PrivilegeInfo).Assembly;

        var guardedMethods = assembly.GetTypes()
            .SelectMany(type => type.GetMethods())
            .Where(method => method.GetCustomAttributes<AuthorizeAttribute>(false).Any())
            .ToArray();

        var descriptions = LoadDescriptions();
        var result = new List<PrivilegeInfo>();

        foreach (var method in guardedMethods)
        {
            var policy = method.GetCustomAttributes<AuthorizeAttribute>(false)
                .Select(attribute => attribute.Policy)
                .FirstOrDefault(name => !string.IsNullOrEmpty(name));
            if (policy == null) continue;

            var level = PolicyPrivileges.TryGetValue(policy, out var mapped) ? mapped : UserPrivileges.NotApproved;
            var description = descriptions.TryGetValue(method.Name, out var found) ? found : "";

            result.Add(new PrivilegeInfo(method.Name, method.ReturnType.FullName ?? "", description, level, policy));
        }

        return result;
    }

    private static Dictionary<string, string> LoadDescriptions()
    {
        var descriptions = new Dictionary<string, string>();
        var path = General.XmlMethodDescriptions;
        if (!File.Exists(path)) return descriptions;

        var document = new XmlDocument();
        document.LoadXml(File.ReadAllText(path));
        var nodes = document.SelectNodes("root/data");
        if (nodes == null) return descriptions;

        foreach (XmlNode node in nodes)
        {
            var name = node.Attributes?["name"]?.Value;
            // Indexer assignment rather than Add: a duplicated name in the file used to throw here.
            if (!string.IsNullOrEmpty(name)) descriptions[name] = node.InnerText;
        }

        return descriptions;
    }
}

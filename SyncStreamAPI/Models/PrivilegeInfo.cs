using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;
using SyncStreamAPI.Annotations;
using SyncStreamAPI.Enums;
using SyncStreamAPI.Helper;
using SyncStreamAPI.PostgresModels;

namespace SyncStreamAPI.Models;

public class PrivilegeInfo
{
    private PrivilegeInfo(string methodName, string typeName, string description, UserPrivileges requiredPrivileges,
        AuthenticationType authenticationType)
    {
        MethodName = methodName;
        TypeName = typeName;
        Description = description;
        RequiredPrivileges = requiredPrivileges;
        AuthenticationType = authenticationType;
    }

    public string MethodName { get; set; }
    public string TypeName { get; set; }
    public string Description { get; set; }
    public UserPrivileges RequiredPrivileges { get; set; }
    public AuthenticationType AuthenticationType { get; set; }

    public static List<PrivilegeInfo> GetPrivilegedMethodsInfo()
    {
        var result = new List<PrivilegeInfo>();

        // Get the current assembly
        var assembly = Assembly.GetCallingAssembly();

        // Get all types in the assembly that have the PrivilegeAttribute applied to them
        var methodsWithAttribute = assembly.GetTypes()
            .SelectMany(t => t.GetMethods())
            .Where(m => m.GetCustomAttributes(typeof(PrivilegeAttribute), false).Length > 0)
            .ToArray();

        var xmlDescriptions = General.XmlMethodDescriptions;
        var descriptions = new Dictionary<string, string>();
        if (File.Exists(xmlDescriptions))
        {
            var xml = File.ReadAllText(xmlDescriptions);
            var xmlDoc = new XmlDocument();
            xmlDoc.LoadXml(xml);
            var resources = xmlDoc.SelectNodes("root/data");
            if (resources != null)
                foreach (XmlNode node in resources)
                    if (node.Attributes != null)
                        descriptions.Add(node.Attributes["name"]?.Value!, node.InnerText);
        }

        // Loop through each method and get basic information about it
        foreach (var method in methodsWithAttribute)
        {
            var attribute = method.GetCustomAttribute<PrivilegeAttribute>();
            var methodName = method.Name;
            var typeName = method.ReturnType.FullName;
            if (attribute == null) continue;
            var requiredPrivileges = attribute.RequiredPrivileges;
            var authenticationType = attribute.AuthenticationType;
            var desc = descriptions.TryGetValue(methodName, out var description) ? description : "";
            var methodInfo = new PrivilegeInfo(methodName, typeName, desc, requiredPrivileges, authenticationType);
            result.Add(methodInfo);
        }

        return result;
    }
}
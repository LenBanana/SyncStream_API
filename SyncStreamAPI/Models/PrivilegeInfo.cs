using SyncStreamAPI.Annotations;
using SyncStreamAPI.Enums;
using SyncStreamAPI.Helper;
using SyncStreamAPI.PostgresModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Resources;
using System.Xml;

namespace SyncStreamAPI.Models
{
    public class PrivilegeInfo
    {
        public string MethodName { get; set; }
        public string TypeName { get; set; }
        public string Description { get; set; }
        public UserPrivileges RequiredPrivileges { get; set; }
        public AuthenticationType AuthenticationType { get; set; }

        public PrivilegeInfo(string methodName, string typeName, string description, UserPrivileges requiredPrivileges, AuthenticationType authenticationType)
        {
            MethodName = methodName;
            TypeName = typeName;
            Description = description;
            RequiredPrivileges = requiredPrivileges;
            AuthenticationType = authenticationType;
        }

        public static List<PrivilegeInfo> GetPrivilegedMethodsInfo()
        {
            List<PrivilegeInfo> result = new List<PrivilegeInfo>();

            // Get the current assembly
            Assembly assembly = Assembly.GetCallingAssembly();

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
                XmlNodeList resources = xmlDoc.SelectNodes("root/data");
                foreach (XmlNode node in resources)
                {
                    descriptions.Add(node.Attributes["name"].Value, node.InnerText);
                }
            }

            // Loop through each method and get basic information about it
            foreach (var method in methodsWithAttribute)
            {
                var attribute = method.GetCustomAttribute<PrivilegeAttribute>();
                string methodName = method.Name;
                string typeName = method.ReturnType.FullName;
                UserPrivileges requiredPrivileges = attribute.RequiredPrivileges;
                AuthenticationType authenticationType = attribute.AuthenticationType;
                string desc = descriptions.ContainsKey(methodName) ? descriptions[methodName] : "";
                PrivilegeInfo methodInfo = new PrivilegeInfo(methodName, typeName, desc, requiredPrivileges, authenticationType);
                result.Add(methodInfo);
            }

            return result;
        }
    }
}

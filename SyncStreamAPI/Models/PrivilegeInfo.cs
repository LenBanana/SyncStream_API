using SyncStreamAPI.Annotations;
using SyncStreamAPI.Enums;
using SyncStreamAPI.PostgresModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace SyncStreamAPI.Models
{
    public class PrivilegeInfo
    {
        public string MethodName { get; set; }
        public string TypeName { get; set; }
        public UserPrivileges RequiredPrivileges { get; set; }
        public AuthenticationType AuthenticationType { get; set; }

        public PrivilegeInfo(string methodName, string typeName, UserPrivileges requiredPrivileges, AuthenticationType authenticationType)
        {
            MethodName = methodName;
            TypeName = typeName;
            RequiredPrivileges = requiredPrivileges;
            AuthenticationType = authenticationType;
        }

        public static List<PrivilegeInfo> GetPrivilegedMethodsInfo()
        {
            List<PrivilegeInfo> result = new List<PrivilegeInfo>();

            // Get the current assembly
            Assembly assembly = Assembly.GetCallingAssembly();

            // Get all types in the assembly that have the PrivilegeAttribute applied to them
            var typesWithAttribute = assembly.GetTypes()
                                             .Where(t => Attribute.IsDefined(t, typeof(PrivilegeAttribute)));

            // Loop through each type and get its methods that have the PrivilegeAttribute applied to them
            foreach (var type in typesWithAttribute)
            {
                var methodsWithAttribute = type.GetMethods()
                                               .Where(m => Attribute.IsDefined(m, typeof(PrivilegeAttribute)));

                // Loop through each method and get basic information about it
                foreach (var method in methodsWithAttribute)
                {
                    var attribute = method.GetCustomAttribute<PrivilegeAttribute>();
                    string methodName = method.Name;
                    string typeName = type.Name;
                    UserPrivileges requiredPrivileges = attribute.RequiredPrivileges;
                    AuthenticationType authenticationType = attribute.AuthenticationType;
                    PrivilegeInfo methodInfo = new PrivilegeInfo(methodName, typeName, requiredPrivileges, authenticationType);
                    result.Add(methodInfo);
                }
            }

            return result;
        }
    }
}

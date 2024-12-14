using System;
using AspectInjector.Broker;

namespace SyncStreamAPI.Annotations;

[Aspect(Scope.Global)]
[Injection(typeof(ErrorHandlingAttribute))]
public class ErrorHandlingAttribute : Attribute
{
    [Advice(Kind.Around, Targets = Target.Method)]
    public object PrivilegeEnter(
        [Argument(Source.Name)] string name,
        [Argument(Source.Arguments)] object[] args,
        [Argument(Source.Target)] Func<object[], object> target)
    {
        try
        {
            var result = target(args);
            return result;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in '{name}'");
            Console.WriteLine(ex.ToString());
            throw;
        }
    }
}
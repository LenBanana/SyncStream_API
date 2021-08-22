using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SyncStreamAPI.Helper
{
    public static class StringExtensions
    {
        public static string FirstCharToUpper(this string input) => input.First().ToString().ToUpper() + input.Substring(1);

    }
}

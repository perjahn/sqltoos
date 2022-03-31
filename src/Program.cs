using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Threading.Tasks;

namespace sqltoelastic
{
    static class Program
    {
        static async Task<int> Main(string[] args)
        {
            if (args.Length != 1)
            {
                Console.WriteLine("Usage: sqltoelastic <configfile>");
                return 1;
            }

            string configfile = args[0];
            if (!File.Exists(configfile))
            {
                Console.WriteLine($"Configfile not found: '{configfile}'");
                return 1;
            }

            var config = JObject.Parse(File.ReadAllText(configfile));

            bool result = await CopyData.CopyRows(config);

            return result ? 0 : 1;
        }
    }
}

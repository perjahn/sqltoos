using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace sqltoelastic
{
    static class Program
    {
        static readonly JsonSerializerOptions options = new();

        static async Task<int> Main(string[] args)
        {
            if (args.Length != 1)
            {
                Log("Usage: sqltoelastic <configfile>");
                return 1;
            }

            var configfile = args[0];
            if (!File.Exists(configfile))
            {
                Log($"Config file not found: '{configfile}'");
                return 1;
            }

            var json = File.ReadAllText(configfile);

            Config? config;
            options.Converters.Add(new EnvironmentVariableConverter<Config>());
            try
            {
                config = JsonSerializer.Deserialize<Config>(json, options);
            }
            catch (JsonException ex)
            {
                Log($"Error: Invalid json in config file: {ex.Message.Trim()} '{configfile}' '{json}'");
                return 1;
            }
            if (config == null)
            {
                Log($"Error: Invalid json in config file: '{configfile}' '{json}'");
                return 1;
            }

            var result = await CopyData.CopyRows(config);

            return result ? 0 : 1;
        }

        static void Log(string message)
        {
            Console.WriteLine(message);
        }
    }
}

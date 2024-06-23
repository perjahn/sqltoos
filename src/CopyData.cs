using System;
using System.Diagnostics;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace sqltoelastic
{
    class CopyData
    {
        public static async Task<bool> CopyRows(JsonObject config)
        {
            Log("Starting...");

            string dbprovider = config["dbprovider"]?.GetValue<string>() ?? string.Empty;
            string connstr = config["connstr"]?.GetValue<string>() ?? string.Empty;
            string sql = config["sql"]?.GetValue<string>() ?? string.Empty;

            string[] toupperfields = GetConfigArray(config, "toupperfields");
            string[] tolowerfields = GetConfigArray(config, "tolowerfields");
            string[] addconstantfields = GetConfigArray(config, "addconstantfields");
            string[] expandjsonfields = GetConfigArray(config, "expandjsonfields");
            string[] deescapefields = GetConfigArray(config, "deescapefields");

            JsonObject[] jsonrows;

            var watch = Stopwatch.StartNew();

            jsonrows = await SqlDB.GetRows(dbprovider, connstr, sql, toupperfields, tolowerfields,
                addconstantfields, expandjsonfields, deescapefields);

            Log($"Got {jsonrows.Length} rows.");

            string serverurl = config["elasticserverurl"]?.GetValue<string>() ?? string.Empty;
            string cacertfile = config["cacertfile"]?.GetValue<string>() ?? string.Empty;
            bool allowInvalidHttpsCert = config["allowinvalidhttpscert"]?.GetValue<bool>() ?? false;
            string username = config["username"]?.GetValue<string>() ?? string.Empty;
            string password = config["password"]?.GetValue<string>() ?? string.Empty;
            string indexname = config["indexname"]?.GetValue<string>() ?? string.Empty;
            string timestampfield = config["timestampfield"]?.GetValue<string>() ?? string.Empty;
            string idfield = config["idfield"]?.GetValue<string>() ?? string.Empty;
            string idprefix = config["idprefix"]?.GetValue<string>() ?? string.Empty;

            bool result = await Elastic.PutIntoIndex(serverurl, cacertfile, allowInvalidHttpsCert, username, password, indexname, timestampfield, idfield, idprefix, jsonrows);

            Log($"Time: {watch.Elapsed}");

            Log("Done!");

            return result;
        }

        static string[] GetConfigArray(JsonObject config, string fieldname)
        {
            return [.. config[fieldname]?.AsArray()?.Select(v => v?.GetValue<string>() ?? string.Empty) ?? []];
        }

        static void Log(string message)
        {
            Console.WriteLine(message);
        }
    }
}

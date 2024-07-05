using System;
using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace sqltoelastic
{
    class CopyData
    {
        public static async Task<bool> CopyRows(Config config)
        {
            Log("Starting...");

            string dbprovider = config.Dbprovider;
            string connstr = config.Connstr;
            string sql = config.Sql;

            string[] toupperfields = config.Toupperfields;
            string[] tolowerfields = config.Tolowerfields;
            string[] addconstantfields = config.Addconstantfields;
            string[] expandjsonfields = config.Expandjsonfields;
            string[] deescapefields = config.Deescapefields;

            JsonObject[] jsonrows;

            var watch = Stopwatch.StartNew();

            jsonrows = await SqlDB.GetRows(dbprovider, connstr, sql, toupperfields, tolowerfields,
                addconstantfields, expandjsonfields, deescapefields);

            Log($"Got {jsonrows.Length} rows.");

            string serverurl = config.Elasticserverurl;
            string cacertfile = config.Cacertfile;
            bool allowInvalidHttpsCert = config.Allowinvalidhttpscert;
            string username = config.Username;
            string password = config.Password;
            string indexname = config.Indexname;
            string timestampfield = config.Timestampfield;
            string idfield = config.Idfield;
            string idprefix = config.Idprefix;

            bool result = await Elastic.PutIntoIndex(serverurl, cacertfile, allowInvalidHttpsCert, username, password, indexname, timestampfield, idfield, idprefix, jsonrows);

            Log($"Time: {watch.Elapsed}");

            Log("Done!");

            return result;
        }

        static void Log(string message)
        {
            Console.WriteLine(message);
        }
    }
}

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

            var dbprovider = config.Dbprovider;
            var connstr = config.Connstr;
            var sql = config.Sql;

            var toupperfields = config.Toupperfields;
            var tolowerfields = config.Tolowerfields;
            var addconstantfields = config.Addconstantfields;
            var expandjsonfields = config.Expandjsonfields;
            var deescapefields = config.Deescapefields;

            JsonObject[] jsonrows;

            var watch = Stopwatch.StartNew();

            jsonrows = await SqlDB.GetRows(dbprovider, connstr, sql, toupperfields, tolowerfields, addconstantfields, expandjsonfields, deescapefields);

            Log($"Got {jsonrows.Length} rows.");

            var serverurl = config.Elasticserverurl;
            var cacertfile = config.Cacertfile;
            var allowInvalidHttpsCert = config.Allowinvalidhttpscert;
            var username = config.Username;
            var password = config.Password;
            var indexname = config.Indexname;
            var timestampfield = config.Timestampfield;
            var idfield = config.Idfield;
            var idprefix = config.Idprefix;

            var result = await Elastic.PutIntoIndex(serverurl, cacertfile, allowInvalidHttpsCert, username, password, indexname, timestampfield, idfield, idprefix, jsonrows);

            Log($"Done: {watch.Elapsed}");

            return result;
        }

        static void Log(string message)
        {
            Console.WriteLine(message);
        }
    }
}

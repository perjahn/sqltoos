using MySql.Data.MySqlClient;
using Npgsql;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Data.SqlClient;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace sqltoelastic
{
    class SqlDB
    {
        public static async Task<JsonObject[]> GetRows(string dbprovider, string connstr, string sql,
            string[] toupperfields, string[] tolowerfields,
            string[] addconstantfields, string[] expandjsonfields, string[] deescapefields)
        {
            DbProviderFactory factory = dbprovider switch
            {
                "mysql" => MySqlClientFactory.Instance,
                "postgres" => NpgsqlFactory.Instance,
                "sqlserver" => SqlClientFactory.Instance,
                _ => throw new Exception()
            };

            using var cn = factory.CreateConnection() ?? throw new Exception();
            cn.ConnectionString = connstr;
            await cn.OpenAsync();

            using var cmd = cn.CreateCommand();

            cmd.Connection = cn;
            cmd.CommandType = CommandType.Text;
            cmd.CommandText = sql;

            using var reader = await cmd.ExecuteReaderAsync();

            var jsonrows = await ReadData(reader, toupperfields, tolowerfields, addconstantfields, expandjsonfields, deescapefields);

            await reader.CloseAsync();

            await cn.CloseAsync();

            return jsonrows;
        }

        static async Task<JsonObject[]> ReadData(DbDataReader reader,
            string[] toupperfields, string[] tolowerfields,
            string[] addconstantfields, string[] expandjsonfields, string[] deescapefields)
        {
            var addfields = addconstantfields.Where(f => f.Contains('=')).ToDictionary(f => f.Split('=')[0], f => f.Split('=')[1]);

            var columns = new List<string>();

            for (int i = 0; i < reader.FieldCount; i++)
            {
                columns.Add(reader.GetName(i));
            }

            var jsonrows = new List<JsonObject>();

            while (await reader.ReadAsync())
            {
                var jsonrow = new JsonObject();

                for (var i = 0; i < reader.FieldCount; i++)
                {
                    var colname = columns[i];

                    if (reader.IsDBNull(i))
                    {
                        continue;
                    }

                    if (reader.GetFieldType(i) == typeof(DateTime))
                    {
                        if (reader.GetValue(i) is DateTime data)
                        {
                            jsonrow[colname] = data;
                        }
                    }
                    else if (reader.GetFieldType(i) == typeof(DateTimeOffset))
                    {
                        if (reader.GetValue(i) is DateTimeOffset data)
                        {
                            jsonrow[colname] = data;
                        }
                    }
                    else if (reader.GetFieldType(i) == typeof(short))
                    {
                        if (reader.GetValue(i) is short data)
                        {
                            jsonrow[colname] = data;
                        }
                    }
                    else if (reader.GetFieldType(i) == typeof(int))
                    {
                        if (reader.GetValue(i) is int data)
                        {
                            jsonrow[colname] = data;
                        }
                    }
                    else if (reader.GetFieldType(i) == typeof(long))
                    {
                        if (reader.GetValue(i) is long data)
                        {
                            jsonrow[colname] = data;
                        }
                    }
                    else if (reader.GetValue(i) is string data)
                    {
                        if (expandjsonfields.Contains(colname))
                        {
                            try
                            {
                                jsonrow[colname] = JsonNode.Parse(data) ?? data;
                            }
                            catch (JsonException)
                            {
                                // Parse. Or Parse not. There is no TryParse. :(
                                jsonrow[colname] = data;
                            }
                        }
                        else
                        {
                            if (toupperfields.Contains(colname))
                            {
                                data = data.ToUpper();
                            }
                            if (tolowerfields.Contains(colname))
                            {
                                data = data.ToLower();
                            }
                            if (!deescapefields.Contains(colname))
                            {
                                data = data.Replace(@"\", @"\\").Replace("\"", "\\\"");
                            }

                            jsonrow[colname] = data;
                        }
                    }
                }

                foreach (var addfield in addfields)
                {
                    var addfieldname = addfield.Key;
                    var addfieldvalue = addfield.Value;

                    jsonrow[addfieldname] = addfieldvalue switch
                    {
                        _ when DateTime.TryParse(addfieldvalue, out DateTime valuedatetime) => valuedatetime,
                        _ when DateTimeOffset.TryParse(addfieldvalue, out DateTimeOffset valuedatetimeoffset) => valuedatetimeoffset,
                        _ when short.TryParse(addfieldvalue, out short valueshort) => valueshort,
                        _ when int.TryParse(addfieldvalue, out int valueint) => valueint,
                        _ when long.TryParse(addfieldvalue, out long valuelong) => valuelong,
                        _ => addfieldvalue
                    };
                }

                jsonrows.Add(jsonrow);
            }

            return [.. jsonrows];
        }
    }
}

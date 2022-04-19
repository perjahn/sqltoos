using MySql.Data.MySqlClient;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Npgsql;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;

namespace sqltoelastic
{
    class SqlDB
    {
        public static async Task<JObject[]> GetRows(string dbprovider, string connstr, string sql,
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

            using DbConnection? cn = factory.CreateConnection();
            if (cn == null)
            {
                throw new Exception();
            }

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

        static async Task<JObject[]> ReadData(DbDataReader reader,
            string[] toupperfields, string[] tolowerfields,
            string[] addconstantfields, string[] expandjsonfields, string[] deescapefields)
        {
            var addfields = addconstantfields.Where(f => f.Contains('=')).ToDictionary(f => f.Split('=')[0], f => f.Split('=')[1]);

            var columns = new List<string>();

            for (int i = 0; i < reader.FieldCount; i++)
            {
                columns.Add(reader.GetName(i));
            }

            var jsonrows = new List<JObject>();

            while (await reader.ReadAsync())
            {
                var jsonrow = new JObject();

                for (int i = 0; i < reader.FieldCount; i++)
                {
                    string colname = columns[i];
                    bool lastcol = i == reader.FieldCount - 1;

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
                        if (data != null)
                        {
                            if (expandjsonfields.Contains(colname))
                            {
                                try
                                {
                                    JObject jsondata = JObject.Parse(data);
                                    jsonrow[colname] = jsondata;
                                }
                                catch (JsonReaderException)
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
                }

                foreach (var addfield in addfields)
                {
                    string addfieldname = addfield.Key;
                    string addfieldvalue = addfield.Value;

                    if (DateTime.TryParse(addfieldvalue, out DateTime valuedatetime))
                    {
                        jsonrow[addfieldname] = valuedatetime;
                    }
                    else if (DateTimeOffset.TryParse(addfieldvalue, out DateTimeOffset valuedatetimeoffset))
                    {
                        jsonrow[addfieldname] = valuedatetimeoffset;
                    }
                    else if (short.TryParse(addfieldvalue, out short valueshort))
                    {
                        jsonrow[addfieldname] = valueshort;
                    }
                    else if (int.TryParse(addfieldvalue, out int valueint))
                    {
                        jsonrow[addfieldname] = valueint;
                    }
                    else if (long.TryParse(addfieldvalue, out long valuelong))
                    {
                        jsonrow[addfieldname] = valuelong;
                    }
                    else
                    {
                        jsonrow[addfieldname] = addfieldvalue;
                    }
                }

                jsonrows.Add(jsonrow);
            }

            return jsonrows.ToArray();
        }

        static void Log(string message)
        {
            Console.WriteLine(message);
        }
    }
}

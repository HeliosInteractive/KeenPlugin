namespace Helios
{
    namespace Keen
    {
        namespace Cache
        {
            using System;
            using System.IO;
            using UnityEngine;
            using System.Data;
            using Mono.Data.Sqlite;
            using System.Collections.Generic;

            /// <summary>
            /// Cache provider that uses SQLite to store cache entries of Keen.IO
            /// failed requests. Make sure you provide UNIQUE database paths to
            /// each instance of this class so no database-locking issues occur
            /// </summary>
            public class Sqlite : Client.ICacheProvider
            {
                private IDbConnection m_DatabaseConn;
                private IDbCommand m_QueryCommand;
                private bool m_Ready = false;

                public Sqlite(string db)
                {
                    if (String.IsNullOrEmpty(db))
                    {
                        Debug.LogWarning("[Keen.Cache] Using default database for caching. A second instance will not work!");
                        db = Path.Combine(Application.persistentDataPath, "keen.sqlite3");
                    }

                    if (!Directory.Exists(Path.GetDirectoryName(db)))
                    {
                        Debug.LogError("[Keen.Cache] cache directory does not exist.");
                        return;
                    }

                    m_DatabaseConn = new SqliteConnection(string.Format("URI=file:{0}", db));
                    m_DatabaseConn.Open(); //Open connection to the database.

                    if (m_DatabaseConn.State != ConnectionState.Open)
                    {
                        Debug.LogErrorFormat("[Keen.Cache] Database {0} cannot be opened. Do you have it already open in another application?", db);
                        return;
                    }
                    else
                    {
                        Debug.LogFormat("[Keen.Cache] Opened cache database connection successfully: {0}", db);
                    }

                    m_QueryCommand = m_DatabaseConn.CreateCommand();

                    if (m_QueryCommand != null)
                    {
                        m_QueryCommand.Connection = m_DatabaseConn;
                        m_QueryCommand.CommandText = "CREATE TABLE IF NOT EXISTS cache(id INTEGER PRIMARY KEY, name, data);";
                        m_QueryCommand.CommandTimeout = 100;

                        try { m_QueryCommand.ExecuteNonQuery(); }
                        catch (Exception ex)
                        {
                            Debug.LogErrorFormat("[Keen.Cache] Unable to create the database {0} due to {1}", db, ex.Message);
                            return;
                        }
                    }
                    else
                    {
                        Debug.LogError("[Keen.Cache] Cannot create a write command. Aborting.");
                        return;
                    }

                    m_Ready = true;
                }

                public override bool Write(Entry entry)
                {
                    if (!Ready()) return false;

                    m_QueryCommand.CommandText = string.Format("INSERT INTO cache VALUES(NULL, '{0}', '{1}');", entry.name, entry.data);

                    try
                    {
                        if (m_QueryCommand.ExecuteNonQuery() == 0)
                            return false;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogErrorFormat("[Keen.Cache] write query failed: {0}", ex.Message);
                        return false;
                    }

                    return true;
                }

                public override bool Remove(int id)
                {
                    if (!Ready()) return false;

                    m_QueryCommand.CommandText = string.Format("DELETE FROM cache WHERE cache.id={0};", id);

                    try
                    {
                        if (m_QueryCommand.ExecuteNonQuery() == 0)
                            return false;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogErrorFormat("[Keen.Cache] remove query failed: {0}", ex.Message);
                        return false;
                    }

                    return true;
                }

                public override bool Ready()
                {
                    return m_Ready;
                }

                public override bool Read(ref List<Entry> entries, uint count)
                {
                    if (!m_Ready || entries == null) return false;
                    entries.Clear();

                    m_QueryCommand.CommandText = string.Format("SELECT * FROM cache LIMIT {0};", count);

                    try
                    {
                        using (IDataReader reader = m_QueryCommand.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                int id = reader.GetInt32(0);
                                string key = reader.GetString(1);
                                string value = reader.GetString(2);

                                entries.Add(new Entry { id = id, name = key, data = value });
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogErrorFormat("[Keen.Cache] read query failed: {0}", ex.Message);
                        return false;
                    }

                    return true;
                }

                #region IDisposable Support
                private bool m_DisposedValue = false;

                protected virtual void Dispose(bool disposing)
                {
                    if (!m_DisposedValue)
                    {
                        if (disposing)
                        {
                            if (m_DatabaseConn != null &&
                                m_DatabaseConn.State == ConnectionState.Open)
                                m_DatabaseConn.Close();

                            if (m_DatabaseConn != null)
                                m_DatabaseConn.Dispose();

                            if (m_QueryCommand != null)
                                m_QueryCommand.Dispose();

                            m_DatabaseConn = null;
                            m_QueryCommand = null;
                        }

                        m_DisposedValue = true;
                        m_Ready = false;
                    }
                }

                public override void Dispose()
                {
                    Dispose(true);
                }
                #endregion
            }
        }
    }
}

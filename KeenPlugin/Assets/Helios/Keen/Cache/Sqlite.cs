namespace Helios.Keen.Cache
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
        private IDbConnection   m_DatabaseConn;
        private IDbCommand      m_QueryCommand;
        private bool            m_Ready = false;
        private uint            m_Attempts = 9;

        public Sqlite(string db)
        {
            if (string.IsNullOrEmpty(db))
            {
                Debug.LogWarning("[Keen.Cache] Using default database for caching. A second instance will not work!");
                db = Path.Combine(Application.persistentDataPath, "keen.sqlite3");

                if (Path.DirectorySeparatorChar != '/')
                    db = db.Replace('/', Path.DirectorySeparatorChar);
            }

            if (!Directory.Exists(Path.GetDirectoryName(db)))
            {
                Debug.LogError("[Keen.Cache] cache directory does not exist.");
                return;
            }

            try
            {
                m_DatabaseConn = new SqliteConnection(string.Format("URI=file:{0}", db));
                m_DatabaseConn.Open(); //Open connection to the database.
            }
            catch(DllNotFoundException)
            {
                Debug.LogError("[Keen.Cache] sqlite3 DLL is not found.");
                m_DatabaseConn = null;
                return;
            }

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
                m_QueryCommand.CommandText = "CREATE TABLE IF NOT EXISTS cache(id INTEGER PRIMARY KEY, attempts INTEGER DEFAULT 0, data VARCHAR(4096) UNIQUE);";
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

        /// <summary>
        /// Set the maximum number of attempts
        /// a cached entry will be retried.
        /// </summary>
        public uint MaxAttempts
        {
            get { return m_Attempts; }
            set { m_Attempts = value; }
        }

        /// <summary>
        /// Disables max attempts (infinite retries)
        /// </summary>
        public void DisableMaxAttempts()
        {
            MaxAttempts = 0;
        }

        public override bool Write(Entry entry)
        {
            if (!Ready()) return false;

            m_QueryCommand.Parameters.Clear();
            m_QueryCommand.CommandText = "INSERT OR IGNORE INTO cache (data) VALUES (@data); UPDATE cache SET attempts=attempts+1 WHERE data=@data";
            m_QueryCommand.Parameters.Add(new SqliteParameter { ParameterName = "@data", Value = entry.ToString() });

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

        public override bool Remove(Entry entry)
        {
            if (!Ready()) return false;

            m_QueryCommand.Parameters.Clear();
            m_QueryCommand.CommandText = "DELETE FROM cache WHERE data=@data";
            m_QueryCommand.Parameters.Add(new SqliteParameter { ParameterName = "@data", Value = entry.ToString() });

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

            m_QueryCommand.Parameters.Clear();

            if (MaxAttempts > 0)
            {
                m_QueryCommand.CommandText = "SELECT data FROM cache WHERE attempts < @limit ORDER BY RANDOM() LIMIT @count";
                m_QueryCommand.Parameters.Add(new SqliteParameter { ParameterName = "@count", Value = count });
                m_QueryCommand.Parameters.Add(new SqliteParameter { ParameterName = "@limit", Value = MaxAttempts });
            }
            else
            {
                m_QueryCommand.CommandText = "SELECT data FROM cache ORDER BY RANDOM() LIMIT @count";
                m_QueryCommand.Parameters.Add(new SqliteParameter { ParameterName = "@count", Value = count });
            }

            try
            {
                using (IDataReader reader = m_QueryCommand.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        Entry entry = new Entry();
                        entry.FromString(reader.GetString(0));
                        entries.Add(entry);
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
                    if (m_DatabaseConn != null)
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

namespace Helios.Keen
{
    using System;
    using UnityEngine;
    using System.Reflection;
    using System.Collections;
    using System.Collections.Generic;

    public partial class Client : MonoBehaviour
    {
        /// <summary>
        /// Used to hold write-specific Keen.IO project settings.
        /// </summary>
        public class Config
        {
            /// <summary>
            /// can be found in https://keen.io/project/<xxx>
            /// where <xxx> is usually obtained via Keen dashboard.
            /// </summary>
            public string ProjectId;

            /// <summary>
            /// can be found in https://keen.io/project/<xxx>
            /// after you click on "Show API Keys"
            /// </summary>
            public string WriteKey;

            /// <summary>
            /// the callback which is called after every attempt
            /// to send an event to Keen.IO. (optional)
            /// </summary>
            public Action<CallbackData> EventCallback;

            /// <summary>
            /// the interval which "cache sweeping" performs
            /// unit is in seconds (2.0f => 2 seconds)
            /// </summary>
            public float CacheSweepInterval = 15.0f;

            /// <summary>
            /// the number of cache entries which will be cleared
            /// from the cache store on every interval
            /// </summary>
            public uint CacheSweepCount = 10;

            /// <summary>
            /// Optional cache provider
            /// </summary>
            public ICacheProvider CacheInstance;
        }

        /// <summary>
        /// Status of a "sent" event to Keen.
        /// Submitted: successfully sent to Keen.
        /// Cached: failed to be sent to Keen and cached in local DB.
        /// Failed: Permanently failed.
        /// </summary>
        public enum EventStatus
        {
            Submitted,
            Cached,
            Failed
        }

        /// <summary>
        /// Represents an event sent to Keen IO
        /// name is collection name and data is JSON object
        /// </summary>
        public class EventData
        {
            string m_Name;
            string m_Data;

            public string Name
            {
                get { return m_Name; }
                set
                {
                    if (value.Length > NameCharacterLimit)
                    {
                        m_Name = value.Substring(0, (int)NameCharacterLimit);
                        Debug.LogWarning("[Keen] name will be truncated.");
                    }
                    else
                    {
                        m_Name = value;
                        return;
                    }
                }
            }

            public string Data
            {
                get { return m_Data; }
                set
                {
                    if (value.Length > DataCharacterLimit)
                    {
                        m_Data = value.Substring(0, (int)DataCharacterLimit);
                        Debug.LogWarning("[Keen] data will be truncated.");
                    }
                    else
                    {
                        m_Data = value;
                        return;
                    }
                }
            }
            
            public WWW AsWWW(Config config)
            {
                if (config == null)
                    throw new ArgumentNullException("Config must be supplied.");

                var headers = new Dictionary<string, string>();
                headers.Add("Authorization", config.WriteKey);
                headers.Add("Content-Type", "application/json");

                WWW www = new WWW(string.Format("https://api.keen.io/3.0/projects/{0}/events/{1}"
                    , config.ProjectId, Name), System.Text.Encoding.ASCII.GetBytes(Data), headers);

                return www;
            }

            public static uint NameCharacterLimit { get { return 1028; } }
            public static uint DataCharacterLimit { get { return 4096; } }
        }

        /// <summary>
        /// Type passed to ClientSettings.EventCallback after
        /// every attempt to submit events to Keen.
        /// </summary>
        public class CallbackData
        {
            public EventStatus  status          = EventStatus.Failed;
            public EventData    evdata          = null;
        }

        private bool            m_Validated     = false;
        private bool            m_Caching       = false;
        private Config          m_Settings      = null;
        private List<EventData> m_PendingTasks  = new List<EventData>();

        /// <summary>
        /// Instance settings. Use this to provide your Keen project settings.
        /// </summary>
        public Config Settings
        {
            get { return m_Settings; }
            set
            {
                StopAllCoroutines();
                m_Validated = false;
                m_Caching = false;
                m_Settings = value;

                if (Settings == null)
                    Debug.LogError("[Keen] Settings object is empty.");
                else if (string.IsNullOrEmpty(Settings.ProjectId))
                    Debug.LogError("[Keen] project ID is empty.");
                else if (string.IsNullOrEmpty(Settings.WriteKey))
                    Debug.LogError("[Keen] write key is empty.");
                else if (Settings.CacheSweepInterval <= 0.5f)
                    Debug.LogError("[Keen] cache sweep interval is invalid.");
                else m_Validated = true;
            }
        }

        /// <summary>
        /// Start caching routine. You rarely need to call this. SendEvent
        /// calls this when the first time you call SendEvent.
        /// </summary>
        public void StartCaching()
        {
            if (!m_Validated)
            {
                Debug.LogWarning("[Keen] instance is not validated.");
                return;
            }

            if (m_Caching)
            {
                Debug.LogWarning("[Keen] instance is already caching.");
                return;
            }

            if (Settings.CacheInstance != null &&
                Settings.CacheInstance.Ready())
                StartCoroutine(CacheRoutineCo());

            m_Caching = true;
        }

        /// <summary>
        /// Convenience method for SendEvent(string, string)
        /// </summary>
        public void SendEvent<T>(string event_name, T event_data)
        {
            SendEvent(event_name, Serialize(event_data));
        }

        /// <summary>
        /// Sends JSON string to Keen IO
        /// </summary>
        public virtual void SendEvent(string event_name, string event_data)
        {
            if (!m_Validated)
                Debug.LogError("[Keen] Client is not validated.");
            else if (string.IsNullOrEmpty(event_name))
                Debug.LogError("[Keen] event name is empty.");
            else if (string.IsNullOrEmpty(event_data))
                Debug.LogError("[Keen] event data is empty.");
            else // run if all above tests passed
                StartCoroutine(ProcessEventData(new EventData { Name = event_name, Data = event_data }, Settings.EventCallback));

            if (!m_Caching)
                StartCaching();
        }

        /// <summary>
        /// Serializes C# objects to a JSON string using reflection.
        /// NOTE: .NET internally caches reflection info. Do not double
        /// cache it here. ONLY .NET 1.0 does not have reflection cache
        /// NOTE: NO anonymous types!
        /// </summary>
        protected string Serialize<T>(T obj)
        {
            // Take in objects of type struct or class only.
            if (obj == null || !(typeof(T).IsValueType || typeof(T).IsClass))
                return string.Empty;

            string json = "{";

            foreach (FieldInfo info in typeof(T).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                string key = string.Format("\"{0:s}\"", info.Name);
                string val = "\"error\"";

                // This covers all integral types (type double in json)
                if (info.FieldType.IsPrimitive)
                {
                    if (info.FieldType == typeof(bool))
                    {
                        val = info.GetValue(obj).ToString().ToLower();
                    }
                    else
                    {
                        val = string.Format("{0:g}", info.GetValue(obj));
                    }
                }
                // this covers Enums and casts it to an integer
                else if (info.FieldType.IsEnum)
                {
                    val = string.Format("{0:g}", (int)info.GetValue(obj));
                }
                // This handles classes and struct and recurses into Serialize again
                else if (info.FieldType != typeof(string) &&
                    (info.FieldType.IsClass || info.FieldType.IsValueType))
                {
                    // This invokes our Generic method with
                    // a dynamic type info during runtime.
                    val = GetType()
                        .GetMethod(MethodInfo.GetCurrentMethod().Name)
                        .MakeGenericMethod(info.FieldType)
                        .Invoke(this, new object[] { info.GetValue(obj) })
                        .ToString();
                }
                // Handle everything else as a string
                else
                {
                    object runtime_val = info.GetValue(obj);

                    if (runtime_val == null)
                        val = "null";
                    else
                        val = string.Format("\"{0}\"", runtime_val.ToString().Replace("\"", "\\\""));
                }

                string key_val = string.Format("{0}:{1},", key, val);
                json += key_val;
            }

            json = json.TrimEnd(',');
            json += "}";

            return json;
        }

        /// <summary>
        /// Coroutine that concurrently attempts to send events to Keen.
        /// It cached event data on failure.
        /// </summary>
        private IEnumerator ProcessEventData(EventData event_data, Action<CallbackData> callback)
        {
            if (!m_Validated)
                yield break;

            using (WWW task_www = event_data.AsWWW(Settings))
            {
                m_PendingTasks.Add(event_data);

                yield return task_www;

                m_PendingTasks.Remove(event_data);

                EventStatus event_status = EventStatus.Failed;

                if (!string.IsNullOrEmpty(task_www.error))
                {
                    Debug.LogErrorFormat("[Keen] error: {0}", task_www.error);

                    if (CacheEventData(event_data))
                    {
                        Debug.LogFormat("[Keen] event cached: {0}", event_data.Name);
                        event_status = EventStatus.Cached;
                    }
                    else
                    {
                        Debug.LogFormat("[Keen] event failed: {0}", event_data.Name);
                        event_status = EventStatus.Failed;
                    }
                }
                else
                {
                    Debug.LogFormat("[Keen] event submitted: {0}", event_data.Name);
                    event_status = EventStatus.Submitted;

                    if (Settings.CacheInstance != null &&
                        Settings.CacheInstance.Ready())
                    {
                        if (Settings.CacheInstance.Exists(event_data))
                            Settings.CacheInstance.Remove(event_data);
                    }
                }

                if (callback != null)
                {
                    callback.Invoke(new CallbackData
                    {
                        evdata = event_data,
                        status = event_status
                    });
                }
            }
        }

        /// <summary>
        /// Cached EventData for later use
        /// </summary>
        /// <param name="event_data">event to be cached</param>
        /// <returns>answers true on successful cache</returns>
        private bool CacheEventData(EventData event_data)
        {
            bool success = false;

            if (!m_Validated ||
                event_data == null)
            {
                Debug.LogError("[Keen] data is null and cannot be cached.");
            }
            else
            if (Settings.CacheInstance != null &&
                Settings.CacheInstance.Ready())
            {
                success = Settings.CacheInstance.Write(event_data);
            }

            return success;
        }

        /// <summary>
        /// Coroutine that takes care of cached events progressively
        /// </summary>
        private IEnumerator CacheRoutineCo()
        {
            yield return new WaitForSeconds(Settings.CacheSweepInterval);

            if (!m_Validated ||
                Settings.CacheInstance == null ||
                Settings.CacheInstance.Ready())
            {
                Debug.LogError("[Keen] cache routine is going to die forever. Bye Bye.");
                yield break;
            }

            List<EventData> cached_events = new List<EventData>();
            if (Settings.CacheInstance.Read(ref cached_events, Settings.CacheSweepCount) && cached_events.Count > 0)
            {
                foreach (EventData entry in cached_events)
                    yield return ProcessEventData(entry, null);
            }

            yield return CacheRoutineCo();
        }

        /// <summary>
        /// Make sure everything is stopped when object is gone.
        /// </summary>
        private void OnDestroy()
        {
            StopAllCoroutines();

            if (m_PendingTasks.Count > 0)
            {
                Debug.LogWarningFormat("[Keen] you have pending events while shutting down! They will be cached.");

                foreach (EventData pending_task in m_PendingTasks)
                    CacheEventData(pending_task);

                m_PendingTasks.Clear();
            }

            if (Settings != null && Settings.CacheInstance != null)
                Settings.CacheInstance.Dispose();
        }

        /// <summary>
        /// Interface defining a possible cache provider
        /// for KeenClient. A possible implementation is
        /// found in the optional KeenClientFileCache.cs
        /// </summary>
        public abstract class ICacheProvider : IDisposable
        {
            /// <summary>
            /// Reads "count" number of cache entries and fills
            /// the passed in List. Returns success of operation.
            /// </summary>
            public abstract bool Read(ref List<EventData> entries, uint count);

            /// <summary>
            /// Writes an "Entry" to cache.
            /// NOTE: MUST handle double-entries properly!
            /// </summary>
            public abstract bool Write(EventData entry);

            /// <summary>
            /// Removes an entry with ID "id" from cache storage.
            /// </summary>
            public abstract bool Remove(EventData entry);

            /// <summary>
            /// Removes an entry with ID "id" from cache storage.
            /// </summary>
            public abstract bool Exists(EventData entry);

            /// <summary>
            /// Answers true if instance is ready to cache.
            /// </summary>
            public abstract bool Ready();

            /// <summary>
            /// Will be called when cache is no longer needed.
            /// </summary>
            public abstract void Dispose();
        }
    }
}

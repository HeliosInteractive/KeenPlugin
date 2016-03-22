namespace Helios
{
    namespace Keen
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
                Failed,
                None
            }

            /// <summary>
            /// Type passed to ClientSettings.EventCallback after
            /// every attempt to submit events to Keen.
            /// </summary>
            public class CallbackData
            {
                public EventStatus  status;
                public string       name;
                public string       data;
            }

            private bool                        m_Validated = false;
            private Config                      m_Settings  = null;
            private List<ICacheProvider.Entry>  m_Cached    = new List<ICacheProvider.Entry>();

            public Config Settings
            {
                get { return m_Settings; }
                set
                {
                    StopAllCoroutines();
                    m_Validated = false;
                    m_Settings = value;

                    if (Settings == null)
                        Debug.LogError("[Keen] Settings object is empty.");
                    else if (String.IsNullOrEmpty(Settings.ProjectId))
                        Debug.LogError("[Keen] project ID is empty.");
                    else if (String.IsNullOrEmpty(Settings.WriteKey))
                        Debug.LogError("[Keen] write key is empty.");
                    else if (Settings.CacheSweepInterval <= 0.5f)
                        Debug.LogError("[Keen] cache sweep interval is invalid.");
                    else m_Validated = true;

                    if (Settings.CacheInstance != null &&
                        Settings.CacheInstance.Ready())
                        StartCoroutine(CacheRoutineCo());
                }
            }

            /// <summary>
            /// Serializes C# objects to a JSON string using reflection.
            /// NOTE: .NET internally caches reflection info. Do not double
            /// cache it here. ONLY .NET 1.0 does not have reflection cache
            /// NOTE: NO anonymous types!
            /// </summary>
            public string Serialize<T>(T obj)
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
                        val = typeof(Client)
                            .GetMethod("Serialize")
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
            /// Convenience method for SendEvent(string, string)
            /// </summary>
            public void SendEvent<T>(string event_name, T event_data)
            {
                SendEvent(event_name, Serialize(event_data));
            }

            /// <summary>
            /// Sends JSON string to Keen IO
            /// </summary>
            public void SendEvent(string event_name, string event_data)
            {
                if (!m_Validated)
                    Debug.LogError("[Keen] Client is not validated.");
                else if (String.IsNullOrEmpty(event_name))
                    Debug.LogError("[Keen] event name is empty.");
                else if (String.IsNullOrEmpty(event_data))
                    Debug.LogError("[Keen] event data is empty.");
                else // run if all above tests passed
                    StartCoroutine(SendEventCo(event_name, event_data, Settings.EventCallback));
            }

            /// <summary>
            /// Coroutine that concurrently attempts to send events to Keen.
            /// </summary>
            IEnumerator SendEventCo(string event_name, string event_data, Action<CallbackData> callback, EventStatus status = EventStatus.None)
            {
                if (!m_Validated)
                    yield break;

                var headers = new Dictionary<string, string>();
                headers.Add("Authorization", Settings.WriteKey);
                headers.Add("Content-Type", "application/json");

                WWW keen_server = new WWW(string.Format("https://api.keen.io/3.0/projects/{0}/events/{1}"
                    , Settings.ProjectId, event_name),
                    System.Text.Encoding.ASCII.GetBytes(event_data), headers);

                yield return keen_server;

                if (!String.IsNullOrEmpty(keen_server.error))
                {
                    Debug.LogErrorFormat("[Keen]: {0}", keen_server.error);

                    if (status == EventStatus.None &&
                        Settings.CacheInstance != null &&
                        Settings.CacheInstance.Ready() &&
                        Settings.CacheInstance.Write(new ICacheProvider.Entry { name = event_name, data = event_data }))
                    {
                        if (callback != null)
                            callback.Invoke(new CallbackData
                            { status = EventStatus.Cached, data = event_data, name = event_name });
                    }
                    else if (callback != null)
                        callback.Invoke(new CallbackData
                        { status = EventStatus.Failed, data = event_data, name = event_name });
                }
                else
                {
                    Debug.LogFormat("[Keen] sent successfully: {0}", event_name);
                    if (callback != null)
                        callback.Invoke(new CallbackData
                        { status = EventStatus.Submitted, data = event_data, name = event_name });
                }
            }

            /// <summary>
            /// Coroutine that takes care of cached events progressively
            /// </summary>
            IEnumerator CacheRoutineCo()
            {
                if (!m_Validated ||
                    Settings.CacheInstance == null ||
                    !Settings.CacheInstance.Ready())
                    yield break;

                if (Settings.CacheInstance.Read(ref m_Cached, Settings.CacheSweepCount))
                {
                    foreach (ICacheProvider.Entry entry in m_Cached)
                    {
                        yield return SendEventCo(entry.name, entry.data, (result) =>
                        {
                            if (result.status == EventStatus.Submitted)
                            {
                                Debug.Log("[Keen] Cached event sent successfully and will be removed.");
                                Settings.CacheInstance.Remove(entry.id);
                            }
                            else
                            {
                                Debug.LogWarningFormat("[Keen] Cached event with id {0} failed to be sent.",
                                    entry.id);
                            }
                        },
                        EventStatus.Cached);
                    }
                }

                yield return new WaitForSeconds(Settings.CacheSweepInterval);
                yield return CacheRoutineCo();
            }

            /// <summary>
            /// Make sure everything is stopped when object is gone.
            /// </summary>
            void OnDestroy()
            {
                StopAllCoroutines();

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
                /// Represents a cache entry
                /// </summary>
                public class Entry
                {
                    public int id;
                    public string name;
                    public string data;
                }

                /// <summary>
                /// Reads "count" number of cache entries and fills
                /// the passed in List. Returns success of operation.
                /// </summary>
                public abstract bool Read(ref List<Entry> entries, uint count);

                /// <summary>
                /// Writes an "Entry" to cache.
                /// </summary>
                public abstract bool Write(Entry entry);

                /// <summary>
                /// Removes an entry with ID "id" from cache storage.
                /// </summary>
                public abstract bool Remove(int id);

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
}

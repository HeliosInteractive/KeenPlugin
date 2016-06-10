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
        private bool                        m_Caching = false;
        private Config                      m_Settings = null;
        private List<ICacheProvider.Entry>  m_Cached = new List<ICacheProvider.Entry>();
        private List<Request>               m_RequestQueue = new List<Request>();

        /// <summary>
        /// Encapsulates data of a Keen IO event
        /// network request through this library
        /// </summary>
        private class Request : IDisposable
        {
            private WWW                     m_RequestData;
            private string                  m_EventName;
            private string                  m_EventData;
            private Action<CallbackData>    m_Callback;
            private EventStatus             m_Status;

            public WWW                      RequestData { get { return m_RequestData; } }
            public string                   EventName { get { return m_EventName; } }
            public string                   EventData { get { return m_EventData; } }
            public Action<CallbackData>     Callback { get { return m_Callback; } }
            public EventStatus              Status { get { return m_Status; } }

            public Request(
                WWW request_data,
                string event_name,
                string event_data,
                Action<CallbackData> callback,
                EventStatus status)
            {
                m_RequestData = request_data;
                m_EventName = event_name;
                m_EventData = event_data;
                m_Callback = callback;
                m_Status = status;
            }

            public bool IsDone
            {
                get
                {
                    return m_RequestData != null && m_RequestData.isDone;
                }
            }

            #region IDisposable Support
            private bool m_Disposed = false;

            protected virtual void Dispose(bool disposing)
            {
                if (!m_Disposed)
                {
                    if (disposing)
                    {
                        if (m_RequestData != null)
                            m_RequestData.Dispose();
                    }

                    m_RequestData = null;
                    m_Disposed = true;
                }
            }

            public void Dispose()
            {
                Dispose(true);
            }
            #endregion
        }

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
                StartCoroutine(SendEventCo(event_name, event_data, Settings.EventCallback));

            if (!m_Caching)
                StartCaching();
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

            using (Request request = new Request(keen_server, event_name, event_data, callback, status))
            {
                m_RequestQueue.Add(request);

                yield return keen_server;

                m_RequestQueue.Remove(request);

                if (!CheckRequest(request))
                    CacheRequest(request);
            }
        }

        /// <summary>
        /// Checks if a completed (isDone == true) request succeeded.
        /// </summary>
        /// <param name="request">A Request object</param>
        /// <returns>True on a successful request; otherwise, false.</returns>
        private bool CheckRequest(Request request)
        {
            if (request == null)
                throw new NullReferenceException("[Keen] Request is null.");

            if (!request.IsDone)
                throw new InvalidOperationException("[Keen] Request is not complete.");

            if (!string.IsNullOrEmpty(request.RequestData.error))
            {
                Debug.LogErrorFormat("[Keen] error: {0}", request.RequestData.error);
                return false;
            }
            else
            {
                Debug.LogFormat("[Keen] sent successfully: {0}", request.EventName);
                if (request.Callback != null)
                    request.Callback.Invoke(new CallbackData
                    { status = EventStatus.Submitted, data = request.EventData, name = request.EventName });
            }

            return true;
        }

        /// <summary>
        /// Checks if the queued request failed and if so, caches it.
        /// </summary>
        /// <param name="request">A Request object</param>
        private void CacheRequest(Request request)
        {
            if (request == null)
                throw new NullReferenceException("[Keen] Request is null.");

            if (request.Status == EventStatus.None &&
                Settings.CacheInstance != null &&
                Settings.CacheInstance.Ready() &&
                Settings.CacheInstance.Write(new ICacheProvider.Entry { name = request.EventName, data = request.EventData }))
            {
                if (request.Callback != null)
                    request.Callback.Invoke(new CallbackData
                    { status = EventStatus.Cached, data = request.EventData, name = request.EventName });
            }
            else if (request.Callback != null)
                request.Callback.Invoke(new CallbackData
                { status = EventStatus.Failed, data = request.EventData, name = request.EventName });
        }

        /// <summary>
        /// Coroutine that takes care of cached events progressively
        /// </summary>
        IEnumerator CacheRoutineCo()
        {
            yield return new WaitForSeconds(Settings.CacheSweepInterval);

            if (!m_Validated ||
                Settings.CacheInstance == null ||
                !Settings.CacheInstance.Ready())
                yield break;

            if (Settings.CacheInstance.Read(ref m_Cached, Settings.CacheSweepCount) && m_Cached.Count > 0)
            {
                foreach (ICacheProvider.Entry entry in m_Cached)
                {
                    yield return SendEventCo(entry.name, entry.data, (result) =>
                    {
                        if (result.status == EventStatus.Submitted)
                        {
                            Debug.Log("[Keen] Cached event sent successfully and will be removed.");
                            Settings.CacheInstance.Remove(entry);
                        }
                        else
                        {
                            Debug.LogWarningFormat("[Keen] Cached event failed to be sent.");
                            Settings.CacheInstance.Write(entry);
                        }
                    },
                    EventStatus.Cached);
                }
            }

            yield return CacheRoutineCo();
        }

        /// <summary>
        /// Make sure everything is stopped when object is gone.
        /// </summary>
        void OnDestroy()
        {
            StopAllCoroutines();

            if (m_RequestQueue.Count > 0)
            {
                Debug.LogWarningFormat("[Keen] you have pending events while shutting down! They will be cahced.");

                foreach (Request pending_request in m_RequestQueue)
                {
                    if (!pending_request.IsDone)
                        CacheRequest(pending_request);

                    pending_request.Dispose();
                }

                m_RequestQueue.Clear();
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
            /// Represents a cache entry
            /// </summary>
            public class Entry
            {
                public string name;
                public string data;

                private const string GLUE = "@@@";
                private const uint CHRLIM = 4096;

                /// <summary>
                /// Serializes Entry to string. Uses a GLUE string (above)
                /// NOTE: if you happen to have the GLUE string in your data
                /// it will be removed!
                /// NOTE: max (data+glue+name) length is CHRLIM (above)
                /// </summary>
                /// <returns>serialized Entry</returns>
                public override string ToString()
                {
                    if (data.Contains(GLUE))
                        Debug.LogWarningFormat("[Keen] your data contains the GLUE string: {0}!", GLUE);

                    string result = string.Format("{0}{1}{2}", name, GLUE, data.Replace(GLUE, string.Empty));

                    if (result.Length > CHRLIM)
                        Debug.LogWarningFormat("[Keen] your data will be truncated! limit is: {0}", CHRLIM);

                    return result;
                }

                /// <summary>
                /// De serializes Entry from a string
                /// </summary>
                /// <param name="input">serialized Entry</param>
                public void FromString(string input)
                {
                    name = string.Empty;
                    data = string.Empty;

                    var splitted = input.Split(new string[] { GLUE }, StringSplitOptions.RemoveEmptyEntries);

                    if (splitted.Length > 0) name = splitted[0];
                    if (splitted.Length > 1) data = splitted[1];
                }
            }

            /// <summary>
            /// Reads "count" number of cache entries and fills
            /// the passed in List. Returns success of operation.
            /// </summary>
            public abstract bool Read(ref List<Entry> entries, uint count);

            /// <summary>
            /// Writes an "Entry" to cache.
            /// NOTE: MUST handle double-entries properly!
            /// </summary>
            public abstract bool Write(Entry entry);

            /// <summary>
            /// Removes an entry with ID "id" from cache storage.
            /// </summary>
            public abstract bool Remove(Entry entry);

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

namespace Helios.Keen
{
    using UnityEngine;

    /// <summary>
    /// This part of the extension is created based on
    /// Helios Keen.IO Standard events document available at:
    /// https://docs.google.com/spreadsheets/d/12d2cA6EUVbVklkkf27bf9qu6nPy39BEBXj1RhxYHc8c
    /// </summary>
    public partial class Client : MonoBehaviour
    {
        public enum RegisterStatus
        {
            NewGuest                = 0,
            ReturningGuest          = 1,
            OptedOut                = 2,
        }

        public enum EndSessionType
        {
            Abandoned               = 0,
            Completed               = 1,
            Erroneous               = 2
        }

        public struct ExperienceData
        {
            public string           versionNumber;
            public string           experienceLabel;
            public string           location;
        }

        public struct Session
        {
            public string           guestId;
            public float            duration;
            public ExperienceData   experienceData;
            public RegisterStatus   registerStatus;
            public EndSessionType   abandoned;
        }

        public struct QuizEvent
        {
            public string           quizId;
            public string           quizResult;
            public ExperienceData   experienceData;
        }

        public struct QuestionEvent
        {
            public string           quizId;
            public string           questionId;
            public string           questionAnswer;
            public float            questionAnswerValue;
            public ExperienceData   experienceData;
        }

        public struct ActionEvent
        {
            public string           actionId;
            public ExperienceData   experienceData;
        }

        public struct Pages
        {
            public string           pageName;
            public float            duration;
            public ExperienceData   experienceData;
        }

        public void SendQuizEvent(QuizEvent data)           { SendEvent("QuizEvent", data); }
        public void SendQuestionEvent(QuestionEvent data)   { SendEvent("QuestionEvent", data); }
        public void SendActionEvent(ActionEvent data)       { SendEvent("ActionEvent", data); }
        public void SendPages(Pages data)                   { SendEvent("Pages", data); }
    }

    /// <summary>
    /// A Client sub-class which can handle session logic and appends session
    /// UUID to all events sent to Keen IO after a session is started.
    /// </summary>
    public class SessionAwareClient : Client
    {
        private Session m_Session;
        private float   m_StartTime;

        /// <summary>
        /// Call this to start a new session. Once you pass in your session data
        /// you can no longer modify it. All fields is the passed session data are
        /// optional. If you have a guest authenticated, you may supply it to guestId
        /// field otherwise a new UUID will be generated for you.
        /// </summary>
        /// <param name="session">session data to start. all fields are optional</param>
        public void StartSession(Session session = default(Session))
        {
            if (SessionStarted)
                EndSession(EndSessionType.Erroneous);

            if (string.IsNullOrEmpty(session.guestId))
                session.guestId = System.Guid.NewGuid().ToString();

            m_StartTime = Time.time;
            m_Session = session;

            base.SendEvent("SessionStart", Serialize(m_Session));
        }

        /// <summary>
        /// End the current active session, previously established with a call to StartSession.
        /// You may supply a specific non-zero duration, otherwise duration will be filled
        /// with the number of seconds since you called StartSession.
        /// </summary>
        /// <param name="type"></param>
        public void EndSession(EndSessionType type)
        {
            if (!SessionStarted)
                return;

            if (m_Session.duration == 0.0f)
                m_Session.duration = Time.time - m_StartTime;

            m_Session.abandoned = type;

            base.SendEvent("SessionEnd", Serialize(m_Session));

            m_Session = default(Session);
            m_StartTime = 0.0f;
        }

        /// <summary>
        /// Answers true if a session is started with StartSession
        /// </summary>
        public bool SessionStarted
        {
            get { return !string.IsNullOrEmpty(m_Session.guestId) && m_StartTime > 0.0f; }
        }

        /// <summary>
        /// Send event override which appends session UUID to every event.
        /// </summary>
        /// <remarks>You cannot call this without calling StartSession first</remarks>
        /// <param name="event_name">event name</param>
        /// <param name="event_data">event json string</param>
        public override void SendEvent(string event_name, string event_data)
        {
            if (!SessionStarted)
            {
                Debug.LogError("[Keen] You are using the session-aware client but a session is not active yet, did you forget to call StartSession?");
                return;
            }

            AppendSessionUuid(ref event_data);
            base.SendEvent(event_name, event_data);
        }

        /// <summary>
        /// Appends session's guestId to event data
        /// </summary>
        /// <param name="data">event data JSON object (note it is object, not array!
        /// must end with a trailing curly brackets).</param>
        private void AppendSessionUuid(ref string data)
        {
            if (string.IsNullOrEmpty(data))
            {
                Debug.LogError("[Keen] JSON string to be sent is empty.");
                return;
            }

            if (!SessionStarted)
            {
                Debug.LogError("[Keen] session is not started yet, did you forget to call StartSession?");
                return;
            }

            data = data.Trim();

            if (!data.EndsWith("}"))
            {
                Debug.LogError("[Keen] your JSON string is not a valid object. Your string must end with a curly bracket.");
                return;
            }

            data = string.Format("{0}, \"guestId\":\"{1}\"}}", data.Remove(data.Length - 1, 1), m_Session.guestId);
        }
    }
}

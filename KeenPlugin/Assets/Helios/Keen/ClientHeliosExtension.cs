namespace Helios
{
    namespace Keen
    {
        using UnityEngine;

        /// <summary>
        /// This part of the extension is created based on
        /// Helios Keen.IO Standard events document available at:
        /// https://docs.google.com/spreadsheets/d/12d2cA6EUVbVklkkf27bf9qu6nPy39BEBXj1RhxYHc8c
        /// </summary>
        public partial class Client : MonoBehaviour
        {
            public struct ExperienceData
            {
                public string           versionNumber;
                public string           experienceLabel;
                public string           location;
            }

            public struct Session
            {
                public float            duration;
                public string           registerStatus;
                public bool             abandoned;
                public ExperienceData   experienceData;
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

            public void SendSession(Session data)               { SendEvent("Session", data); }
            public void SendQuizEvent(QuizEvent data)           { SendEvent("QuizEvent", data); }
            public void SendQuestionEvent(QuestionEvent data)   { SendEvent("QuestionEvent", data); }
            public void SendActionEvent(ActionEvent data)       { SendEvent("ActionEvent", data); }
            public void SendPages(Pages data)                   { SendEvent("Pages", data); }
        }
    }
}

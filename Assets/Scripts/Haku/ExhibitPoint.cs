// ---------------------------------------------------------------------------------------------
// ExhibitPoint.cs - one per exhibit prefab. Carries the exhibit_id, knows whether the visitor is
// holding it and how far away they are, sends their spoken question, and plays the guide's answer
// out of a spatialised AudioSource ON THE OBJECT ITSELF, so the voice comes from the artefact.
//
// Setup on the prefab:
//   1. Add this component (it adds an AudioSource for you).
//   2. Type the Exhibit Id. It MUST match a record id in ai-service/exhibits/*.json exactly
//      (e.g. "bronze-mirror-01"). A typo here is a 404 on every question at this exhibit and is
//      NOT a compile error - check it in review.
//   3. If the object is grabbable, wire XRGrabInteractable in the Inspector:
//        Select Entered -> ExhibitPoint.OnGrabbed
//        Select Exited  -> ExhibitPoint.OnReleased
//
// XRI coupling: this file deliberately references NO XR Interaction Toolkit type. XRI 3.x moved
// XRGrabInteractable into UnityEngine.XR.Interaction.Toolkit.Interactables, so a `using` here would
// break the whole project on a package bump. The two public methods above are the entire coupling
// and it is an Inspector wire, not a compile-time dependency. (Recommended XRGrabInteractable
// settings while you are there: Use Dynamic Attach ON, Attach Ease In Time 0.1-0.2 s,
// Movement Type Kinematic.)
// ---------------------------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace Haku
{
    /// <summary>(question as heard, answer as spoken)</summary>
    [Serializable]
    public class GuideSpokeEvent : UnityEvent<string, string> { }

    [RequireComponent(typeof(AudioSource))]
    public class ExhibitPoint : MonoBehaviour
    {
        [Header("Identity")]
        [Tooltip("Must match the 'id' field of a record in ai-service/exhibits/. Case sensitive.")]
        [SerializeField] private string exhibitId = "bronze-mirror-01";

        [Header("Focus")]
        [Tooltip("A spoken question is routed to the nearest exhibit inside this radius. " +
                 "An exhibit being HELD always wins regardless of radius.")]
        [SerializeField] private float focusRadius = 3f;

        [Tooltip("Leave empty to use Camera.main (the XR Origin's head).")]
        [SerializeField] private Transform visitorHead;

        [Header("Voice")]
        [Tooltip("Where the guide's voice comes from. Leave empty to use this object's AudioSource.")]
        [SerializeField] private AudioSource voiceSource;

        [SerializeField] private float voiceMinDistance = 0.5f;
        [SerializeField] private float voiceMaxDistance = 12f;

        [Header("Events")]
        [Tooltip("Fires when the guide has answered: (question, answer). Femin's UI subscribes here " +
                 "for subtitles. Also available scene-wide as the static event AnyGuideSpoke.")]
        [SerializeField] private GuideSpokeEvent onGuideSpoke = new GuideSpokeEvent();

        [Header("Testing")]
        [Tooltip("Used by the right-click context menu item, so the loop can be tested with no mic.")]
        [SerializeField] private string testQuestion = "What is this made of?";

        /// <summary>Scene-wide notification: (exhibit, question, answer). For a single subtitle panel.</summary>
        public static event Action<ExhibitPoint, string, string> AnyGuideSpoke;

        public string ExhibitId { get { return exhibitId; } }
        public bool IsHeld { get; private set; }
        public bool IsBusy { get; private set; }
        public GuideSpokeEvent OnGuideSpoke { get { return onGuideSpoke; } }

        private static readonly List<ExhibitPoint> Registered = new List<ExhibitPoint>();
        private static bool warnedAboutMissingHead;

        private Camera cachedCamera;

        private void Awake()
        {
            if (voiceSource == null) voiceSource = GetComponent<AudioSource>();

            voiceSource.playOnAwake = false;
            voiceSource.loop = false;
            voiceSource.spatialBlend = 1f;                       // fully 3D - the voice is at the object
            voiceSource.dopplerLevel = 0f;                       // no pitch shift as the visitor walks
            voiceSource.rolloffMode = AudioRolloffMode.Linear;
            voiceSource.minDistance = voiceMinDistance;
            voiceSource.maxDistance = voiceMaxDistance;

            if (string.IsNullOrEmpty(exhibitId))
                Debug.LogError("[Haku] " + name + " has an empty Exhibit Id - every question here will 404.", this);
        }

        private void OnEnable()
        {
            Registered.Add(this);
            MicrophoneCapture.AnyClipReady += HandleClipReady;
        }

        private void OnDisable()
        {
            MicrophoneCapture.AnyClipReady -= HandleClipReady;
            Registered.Remove(this);
        }

        // -----------------------------------------------------------------------------------------
        // Held state - wired from XRGrabInteractable in the Inspector (see the header).
        // -----------------------------------------------------------------------------------------

        public void OnGrabbed()
        {
            IsHeld = true;
        }

        public void OnReleased()
        {
            IsHeld = false;
        }

        // -----------------------------------------------------------------------------------------
        // Focus - which exhibit is the visitor talking to?
        // -----------------------------------------------------------------------------------------

        /// <summary>Metres from the visitor's head to this exhibit, or -1 if the head is unknown.</summary>
        public float DistanceToVisitor()
        {
            Transform head = Head();
            if (head == null) return -1f;
            return Vector3.Distance(head.position, transform.position);
        }

        /// <summary>Held exhibit wins; otherwise the nearest one inside its own focus radius.</summary>
        public static ExhibitPoint FindFocused(Vector3 headPosition)
        {
            ExhibitPoint best = null;
            float bestSqr = float.MaxValue;

            for (int i = 0; i < Registered.Count; i++)
            {
                ExhibitPoint point = Registered[i];
                if (point == null) continue;
                if (point.IsHeld) return point;

                float sqr = (point.transform.position - headPosition).sqrMagnitude;
                if (sqr <= point.focusRadius * point.focusRadius && sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = point;
                }
            }

            return best;
        }

        private Transform Head()
        {
            if (visitorHead != null) return visitorHead;
            if (cachedCamera == null) cachedCamera = Camera.main;
            return cachedCamera != null ? cachedCamera.transform : null;
        }

        // -----------------------------------------------------------------------------------------
        // The loop
        // -----------------------------------------------------------------------------------------

        private void HandleClipReady(AudioClip clip)
        {
            Transform head = Head();
            if (head == null)
            {
                if (!warnedAboutMissingHead)
                {
                    warnedAboutMissingHead = true;
                    Debug.LogError("[Haku] No visitor head transform: assign Visitor Head, or tag the " +
                                   "XR Origin's camera as MainCamera. Questions cannot be routed.", this);
                }
                return;
            }

            ExhibitPoint focused = FindFocused(head.position);

            if (focused == null)
            {
                // Only the first registered exhibit reports this, so one un-routed question produces
                // one warning rather than one per exhibit in the room.
                if (Registered.Count > 0 && Registered[0] == this)
                    Debug.LogWarning("[Haku] A question was recorded but no exhibit is in focus. Walk " +
                                     "closer than the exhibit's Focus Radius, or pick the object up.");
                return;
            }

            if (focused != this) return;

            SendVoiceQuestion(clip);
        }

        /// <summary>Encode a recorded clip and ask the guide about THIS exhibit.</summary>
        public void SendVoiceQuestion(AudioClip clip)
        {
            if (IsBusy)
            {
                Debug.LogWarning("[Haku] " + exhibitId + " is still waiting on the previous answer - " +
                                 "ignoring this one. (Never fire a second request at a stalled service.)");
                return;
            }

            byte[] wav = WavUtility.FromAudioClip(clip);
            if (wav == null) return;

            IsBusy = true;
            float distance = DistanceToVisitor();

            HakuGuideClient.Instance.Converse(
                wav, exhibitId, IsHeld, distance < 0f ? 0f : distance,
                OnGuideReplied,
                error =>
                {
                    IsBusy = false;
                    // The client has already logged url + status + body. This is the visitor-facing half.
                    Debug.LogWarning("[Haku] " + exhibitId + " got no answer: " + error);
                });
        }

        private void OnGuideReplied(GuideReply reply)
        {
            IsBusy = false;
            if (reply == null) return;

            if (reply.clip != null && voiceSource != null)
            {
                voiceSource.Stop();
                voiceSource.clip = reply.clip;
                voiceSource.Play();
            }

            string question = reply.question == null ? "" : reply.question;
            string answer = reply.answer == null ? "" : reply.answer;

            onGuideSpoke.Invoke(question, answer);
            if (AnyGuideSpoke != null) AnyGuideSpoke.Invoke(this, question, answer);
        }

        /// <summary>Stop the guide mid-sentence, e.g. when the visitor pushes to talk again.</summary>
        public void StopSpeaking()
        {
            if (voiceSource != null) voiceSource.Stop();
        }

        // Text-only test path: needs no microphone, no permission, no WAV. Right-click the component
        // header in Play mode. If this works and voice does not, the fault is audio, not networking.
        [ContextMenu("Haku: ask the test question (text only)")]
        private void AskTestQuestion()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[Haku] Enter Play mode first - this sends a real HTTP request.");
                return;
            }

            HakuGuideClient.Instance.AskText(exhibitId, testQuestion,
                answer =>
                {
                    onGuideSpoke.Invoke(answer.question, answer.answer);
                    if (AnyGuideSpoke != null) AnyGuideSpoke.Invoke(this, answer.question, answer.answer);
                },
                error => { /* already logged in detail by HakuGuideClient */ });
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.35f);
            Gizmos.DrawWireSphere(transform.position, focusRadius);
        }
    }
}

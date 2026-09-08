// ---------------------------------------------------------------------------------------------
// MicrophoneCapture.cs - RECORD_AUDIO permission + push-to-talk capture on Quest.
//
// Push-to-talk on a controller button, deliberately NOT amplitude VAD:
//   - zero tuning, and VAD parameters tuned in a quiet lab fail in an assessment room
//   - no false triggers from five teammates talking
//   - deterministic clip start/end, so there is no endpointing bug to debug at 2am
//   - "hold the trigger to talk to the guide" is a legible VR affordance you can demo
//
// Put ONE of these in the scene (50_AI_Guide). It raises the static event AnyClipReady with a
// trimmed AudioClip; ExhibitPoint subscribes to that. It knows nothing about the network.
//
// Wiring the button: assign Talk Action to XRI's "Activate" action on the right controller
// (Starter Assets > XRI Default Input Actions > XRI RightHand Interaction/Activate).
// Alternative with zero input plumbing: call BeginTalk()/EndTalk() from a UI button or another
// script - they are public.
//
// The RECORD_AUDIO permission also has to be in the manifest, not just requested at runtime:
//   <uses-permission android:name="android.permission.RECORD_AUDIO" />
// Dev shortcut if a build is already on the headset:
//   adb shell pm grant com.rmit.haku.museum android.permission.RECORD_AUDIO
// ---------------------------------------------------------------------------------------------
using System;
using System.Collections;
using UnityEngine;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Haku
{
    public class MicrophoneCapture : MonoBehaviour
    {
        /// <summary>Raised when a push-to-talk press produced a usable clip. Static so exhibit
        /// PREFABS can subscribe without holding a scene reference they cannot serialise.</summary>
        public static event Action<AudioClip> AnyClipReady;

        [Header("Push to talk")]
#if ENABLE_INPUT_SYSTEM
        [Tooltip("XRI 'Activate' action on the right controller. Held = recording.")]
        [SerializeField] private InputActionProperty talkAction;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        [Tooltip("Editor/desktop fallback so you can test without a headset.")]
        [SerializeField] private KeyCode editorFallbackKey = KeyCode.T;
#endif

        [Header("Recording")]
        [Tooltip("16 kHz keeps the upload ~3x smaller. The service resamples, so any rate works.")]
        [SerializeField] private int preferredSampleRate = 16000;

        [Tooltip("Hard cap. The service rejects anything much longer, and nobody asks a 12 s question.")]
        [SerializeField] private int maxRecordSeconds = 12;

        [Tooltip("Presses shorter than this are fumbles and are discarded without a request.")]
        [SerializeField] private float minTalkSeconds = 0.4f;

        [Header("Permission")]
        [Tooltip("Wall-clock backstop. If the dialog is dismissed with no answer, no callback ever " +
                 "fires and we would wait forever without this.")]
        [SerializeField] private float permissionTimeoutSeconds = 30f;

        public bool IsRecording { get; private set; }
        public bool MicrophoneReady { get; private set; }
        public string DeviceName { get { return device; } }

        private enum PermissionAnswer { Pending, Granted, Denied }

        private PermissionAnswer answer = PermissionAnswer.Pending;
        private string device;
        private AudioClip recording;
        private int recordingRate;
        private float recordStartTime;

        private IEnumerator Start()
        {
            yield return RequestMicrophonePermission();

            if (answer != PermissionAnswer.Granted)
            {
                Debug.LogError("[Haku] RECORD_AUDIO was not granted - the guide cannot hear anything. " +
                               "Dev workaround: adb shell pm grant com.rmit.haku.museum android.permission.RECORD_AUDIO");
                yield break;
            }

            // Microphone.devices is only populated AFTER the permission is granted on Android.
            string[] devices = Microphone.devices;
            Debug.Log("[Haku] Microphone.devices (" + devices.Length + "): " + string.Join(" | ", devices));

            if (devices.Length == 0)
            {
                Debug.LogError("[Haku] No microphone devices. On Quest this is almost always a missing " +
                               "RECORD_AUDIO entry in Assets/Plugins/Android/AndroidManifest.xml.");
                yield break;
            }

            device = devices[0];
            MicrophoneReady = true;
            Debug.Log("[Haku] Microphone ready: '" + device + "'.");
        }

        private void OnEnable()
        {
#if ENABLE_INPUT_SYSTEM
            if (talkAction.action != null) talkAction.action.Enable();
#endif
        }

        private void OnDisable()
        {
            if (IsRecording) EndTalk();
#if ENABLE_INPUT_SYSTEM
            if (talkAction.action != null) talkAction.action.Disable();
#endif
        }

        private void Update()
        {
            bool wantsToTalk = ReadTalkButton();

            if (wantsToTalk && !IsRecording)
            {
                BeginTalk();
            }
            else if (!wantsToTalk && IsRecording)
            {
                EndTalk();
            }
            else if (IsRecording && Time.realtimeSinceStartup - recordStartTime >= maxRecordSeconds)
            {
                Debug.LogWarning("[Haku] Hit the " + maxRecordSeconds + " s recording cap - sending what we have.");
                EndTalk();
            }
        }

        private bool ReadTalkButton()
        {
            bool pressed = false;
#if ENABLE_INPUT_SYSTEM
            InputAction action = talkAction.action;
            if (action != null && action.enabled) pressed |= action.ReadValue<float>() > 0.5f;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            pressed |= Input.GetKey(editorFallbackKey);
#endif
            return pressed;
        }

        // -----------------------------------------------------------------------------------------
        // Recording
        // -----------------------------------------------------------------------------------------

        public void BeginTalk()
        {
            if (IsRecording) return;

            if (!MicrophoneReady)
            {
                Debug.LogWarning("[Haku] Push-to-talk pressed but the microphone is not ready " +
                                 "(permission denied, or no input device).");
                return;
            }

            recordingRate = ChooseSampleRate();
            recording = Microphone.Start(device, false, maxRecordSeconds, recordingRate);

            if (recording == null)
            {
                Debug.LogError("[Haku] Microphone.Start returned null for device '" + device +
                               "' at " + recordingRate + " Hz.");
                return;
            }

            IsRecording = true;
            recordStartTime = Time.realtimeSinceStartup;
            StartCoroutine(WarnIfMicNeverStarts());
        }

        public void EndTalk()
        {
            if (!IsRecording) return;
            IsRecording = false;

            int position = Microphone.GetPosition(device);
            Microphone.End(device);

            AudioClip captured = recording;
            recording = null;
            if (captured == null) return;

            if (position <= 0)
            {
                Debug.LogError("[Haku] Recording stopped with 0 samples captured on '" + device +
                               "'. The mic opened but never delivered audio.");
                return;
            }

            float seconds = position / (float)recordingRate;
            if (seconds < minTalkSeconds)
            {
                Debug.Log("[Haku] Ignoring a " + seconds.ToString("0.00") + " s press (under the " +
                          minTalkSeconds.ToString("0.00") + " s fumble threshold).");
                return;
            }

            float[] samples = new float[position * captured.channels];
            if (!captured.GetData(samples, 0))
            {
                Debug.LogError("[Haku] GetData failed on the recorded clip.");
                return;
            }

            AudioClip trimmed = AudioClip.Create("HakuQuestion", position, captured.channels,
                                                 recordingRate, false);
            trimmed.SetData(samples, 0);

            Debug.Log("[Haku] Captured " + seconds.ToString("0.00") + " s, " + recordingRate + " Hz, " +
                      captured.channels + " ch, " + position + " frames.");

            if (AnyClipReady != null) AnyClipReady.Invoke(trimmed);
            else Debug.LogWarning("[Haku] A clip was captured but nothing is listening to " +
                                  "MicrophoneCapture.AnyClipReady - is there an ExhibitPoint in the scene?");
        }

        private int ChooseSampleRate()
        {
            int min, max;
            Microphone.GetDeviceCaps(device, out min, out max);
            if (min == 0 && max == 0) return preferredSampleRate;   // 0/0 means "any rate"
            return Mathf.Clamp(preferredSampleRate, min, max);
        }

        private IEnumerator WarnIfMicNeverStarts()
        {
            float deadline = Time.realtimeSinceStartup + 2f;
            while (IsRecording && Microphone.GetPosition(device) <= 0)
            {
                if (Time.realtimeSinceStartup > deadline)
                {
                    Debug.LogError("[Haku] Microphone.Start succeeded on '" + device + "' but the write " +
                                   "head never moved after 2 s. Check RECORD_AUDIO in the manifest and " +
                                   "that no other app holds the mic.");
                    yield break;
                }
                yield return null;
            }
        }

        // -----------------------------------------------------------------------------------------
        // Permission - all four callbacks, plus a wall-clock backstop.
        // Unity 6 raises exactly one of: PermissionGranted / PermissionDenied /
        // PermissionDeniedAndDontAskAgain / PermissionRequestDismissed. The backstop exists because
        // a dialog that never resolves (headset taken off, system UI swallowed it) raises nothing at
        // all, and a coroutine waiting on a flag that never flips hangs the mic for the whole session.
        // -----------------------------------------------------------------------------------------

        public IEnumerator RequestMicrophonePermission()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (Permission.HasUserAuthorizedPermission(Permission.Microphone))
            {
                answer = PermissionAnswer.Granted;
                yield break;
            }

            answer = PermissionAnswer.Pending;

            PermissionCallbacks callbacks = new PermissionCallbacks();

            callbacks.PermissionGranted += permission =>
            {
                Debug.Log("[Haku] Permission granted: " + permission);
                answer = PermissionAnswer.Granted;
            };

            callbacks.PermissionDenied += permission =>
            {
                Debug.LogError("[Haku] Permission DENIED: " + permission +
                               ". The visitor can still walk the museum, but the guide is deaf.");
                answer = PermissionAnswer.Denied;
            };

            callbacks.PermissionDeniedAndDontAskAgain += permission =>
            {
                Debug.LogError("[Haku] Permission denied permanently: " + permission +
                               ". Android will not show the dialog again - grant it in the headset's " +
                               "app settings, or: adb shell pm grant com.rmit.haku.museum " +
                               "android.permission.RECORD_AUDIO");
                answer = PermissionAnswer.Denied;
            };

            callbacks.PermissionRequestDismissed += permission =>
            {
                Debug.LogError("[Haku] Permission dialog dismissed without an answer: " + permission);
                answer = PermissionAnswer.Denied;
            };

            Permission.RequestUserPermission(Permission.Microphone, callbacks);

            float deadline = Time.realtimeSinceStartup + permissionTimeoutSeconds;
            while (answer == PermissionAnswer.Pending)
            {
                // Belt and braces: some Android builds grant without ever invoking the callback.
                if (Permission.HasUserAuthorizedPermission(Permission.Microphone))
                {
                    answer = PermissionAnswer.Granted;
                    break;
                }

                if (Time.realtimeSinceStartup > deadline)
                {
                    Debug.LogError("[Haku] RECORD_AUDIO request produced no answer within " +
                                   permissionTimeoutSeconds + " s. Treating it as denied so the app " +
                                   "keeps running instead of hanging.");
                    answer = PermissionAnswer.Denied;
                    break;
                }

                yield return null;
            }
#else
            // Editor, and the Windows player over Meta Horizon Link: no Android permission model.
            // NOTE: this is exactly why mic + HTTP must be re-verified ON DEVICE before every
            // milestone. Link hides every Android-specific failure in this file.
            answer = PermissionAnswer.Granted;
            yield break;
#endif
        }
    }
}

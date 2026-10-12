using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;

// Put this in:  Assets/Scripts/GuideVoice.cs
//
// Voice for the AI guide (MuseumGuide), fully offline:
//
//   LISTEN  Hold A (right controller) and speak; release to send. The recording goes to a
//           LOCAL whisper.cpp server (whisper-server.exe) which turns it into text.
//   SPEAK   Haku's answers are read out with the Windows built-in voice (System.Speech,
//           which runs on the PC - nothing is sent anywhere).
//
// Works in the Editor / a Windows build with Quest Link: the headset's microphone shows up
// in Windows as "Headset Microphone (Oculus Virtual Audio Device)" and is picked automatically.
//
// SETTINGS are the public fields below (whisperUrl must match how whisper-server was started).

public class GuideVoice : MonoBehaviour
{
    // ------------------------------------------------------------ settings
    public string whisperUrl   = "http://127.0.0.1:8080/inference";
    public string micNameHint  = "Oculus";     // prefer a microphone whose name contains this (falls back to "Headset", then the default)
    public int    maxSeconds   = 15;           // longest question that can be recorded
    public bool   speakAnswers = true;
    public int    voiceRate    = 0;            // -10 (slow) .. 10 (fast)
    public string voiceName    = "";           // empty = first female Windows voice; or e.g. "Microsoft Zira Desktop"

    /// What was heard (already cleaned up). Empty speech is not reported.
    public Action<string> OnHeard;
    /// Short status for the panel ("Listening...", errors).
    public Action<string> OnStatus;
    /// A was only tapped (too short to be speech): nothing to transcribe.
    public Action OnNothingHeard;

    public bool IsRecording { get { return recording; } }
    public bool IsBusy      { get { return transcribing; } }

    const int WhisperRate = 16000;             // whisper wants 16 kHz mono

    AudioSource output;
    string   micDevice;          // the device the ring buffer runs on (null = Windows default)
    AudioClip micClip;           // a LOOPING ring buffer, started once - not once per question
    int      micRate;
    bool     micRunning;
    int      startPos;           // ring-buffer sample where the current question starts
    float    recordStart;
    bool     recording, transcribing;

    System.Diagnostics.Process ttsProcess;
    int      ttsCounter;
    Coroutine speakRoutine;

    [Serializable] class WhisperResp { public string text; public string error; }

    // ============================================================ setup

    public void Init(Transform speaker)
    {
        output = speaker.gameObject.AddComponent<AudioSource>();
        output.playOnAwake  = false;
        output.spatialBlend = 0.6f;            // mostly comes from Haku, but always audible
        output.minDistance  = 1.5f;
        output.maxDistance  = 15f;
        output.dopplerLevel = 0f;              // no pitch wobble when Haku pops to a new spot while talking

        // Start the microphone ONCE as a looping ring buffer. Starting or stopping the Oculus headset
        // mic can block Unity for about a second, so a press of A only marks a position in the buffer.
        if (!EnsureMic())
            Debug.LogWarning("[GuideVoice] No microphone available yet (Windows Settings > Privacy > Microphone). " +
                             "It is tried again whenever A is pressed.");
        Debug.Log("[GuideVoice] Hold A to talk; speech-to-text at " + whisperUrl);
    }

    /// (Re)starts the ring buffer if it is not running or a better microphone has appeared
    /// (e.g. the headset mic after Quest Link connects). The only place Microphone.Start is called.
    bool EnsureMic()
    {
        var devices = Microphone.devices;
        if (devices == null || devices.Length == 0) { StopMic(); return false; }

        string want = PickMicrophone();
        if (micRunning && want == micDevice && Microphone.IsRecording(micDevice)) return true;

        StopMic();
        micDevice = want;
        int min, max;
        Microphone.GetDeviceCaps(micDevice, out min, out max);
        micRate = (min == 0 && max == 0) ? WhisperRate : Mathf.Clamp(WhisperRate, min, max);
        try { micClip = Microphone.Start(micDevice, true, maxSeconds + 2, micRate); }
        catch (Exception e) { Debug.LogWarning("[GuideVoice] Microphone.Start failed: " + e.Message); micClip = null; }
        micRunning = micClip != null;
        if (micRunning) Debug.Log("[GuideVoice] Microphone: " + (micDevice ?? "(system default)"));
        return micRunning;
    }

    void StopMic()
    {
        if (micRunning) { try { Microphone.End(micDevice); } catch { } }
        micRunning = false;
        if (micClip != null) { Destroy(micClip); micClip = null; }
    }

    string PickMicrophone()
    {
        var devices = Microphone.devices;
        if (devices == null || devices.Length == 0)
        {
            Debug.LogWarning("[GuideVoice] No microphone found. Check Windows Settings > Privacy > Microphone.");
            return null;
        }
        foreach (var hint in new[] { micNameHint, "Headset" })
        {
            if (string.IsNullOrEmpty(hint)) continue;
            foreach (var d in devices)
                if (d.IndexOf(hint, StringComparison.OrdinalIgnoreCase) >= 0) return d;
        }
        return null;                           // null = Windows default recording device
    }

    void OnDestroy()
    {
        StopMic();
        KillTts();
    }

    // ============================================================ listening

    void Update()
    {
        if (!recording) return;
        if (!micRunning || !Microphone.IsRecording(micDevice))
        {
            // The microphone went away mid-question (e.g. Quest Link reconnected).
            recording  = false;
            micRunning = false;
            if (output != null) output.UnPause();
            Status("I lost the microphone - hold A and try again.");
            return;
        }
        if (Time.unscaledTime - recordStart >= maxSeconds) EndListening();     // longest question: send it now
    }

    public void BeginListening()
    {
        if (recording) return;
        if (transcribing) { Status("Still working on what you just said..."); return; }
        if (!EnsureMic())
        {
            Status("I can't hear you - no microphone is connected (or it can't be opened).");
            return;
        }

        startPos    = Microphone.GetPosition(micDevice);
        recordStart = Time.unscaledTime;
        // Pause (not stop) Haku's voice: an accidental tap resumes it, real speech stops it.
        if (output != null) output.Pause();
        recording = true;
        Status("Listening... (let go of A when you're done)");
    }

    public void EndListening()
    {
        if (!recording) return;
        recording = false;
        if (!micRunning || micClip == null) return;
        if (Time.unscaledTime - recordStart > maxSeconds + 1.5f)
        {
            // The game stalled (e.g. paused in the Editor) and the ring buffer may have lapped.
            if (output != null) output.UnPause();
            Status("Sorry, that got cut off - hold A and ask again.");
            return;
        }

        // Samples recorded since the press. The ring holds maxSeconds + 2 s and Update ends a
        // question at maxSeconds, so this never laps the start.
        int total   = micClip.samples;
        int samples = (Microphone.GetPosition(micDevice) - startPos + total) % total;

        if (samples < micRate * 0.3f)          // a tap, not speech
        {
            if (output != null) output.UnPause();
            if (OnNothingHeard != null) OnNothingHeard();
            else Status("I didn't catch that - hold A while you speak.");
            return;
        }

        StopSpeaking();                        // real speech: the old answer is no longer wanted

        var data = new float[samples * micClip.channels];
        micClip.GetData(data, startPos);       // wraps round the end of the ring buffer
        float[] mono = ToMono(data, micClip.channels);
        float[] pcm  = Resample(mono, micRate, WhisperRate);

        StartCoroutine(Transcribe(EncodeWav(pcm, WhisperRate)));
    }

    IEnumerator Transcribe(byte[] wav)
    {
        transcribing = true;
        Status("Got it - one moment...");

        var form = new List<IMultipartFormSection>
        {
            new MultipartFormFileSection("file", wav, "question.wav", "audio/wav"),
            new MultipartFormDataSection("response_format", "json"),
            new MultipartFormDataSection("temperature", "0.0"),
        };

        string text = null, problem = null;
        using (var www = UnityWebRequest.Post(whisperUrl, form))
        {
            www.timeout = 60;
            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                problem = "my ears (the Whisper speech server) are not answering at " + whisperUrl +
                          " (" + www.error + "). Is whisper-server running?";
            }
            else
            {
                try
                {
                    var r = JsonUtility.FromJson<WhisperResp>(www.downloadHandler.text);
                    if (r != null && !string.IsNullOrEmpty(r.error)) problem = "speech recognition failed: " + r.error;
                    else text = CleanTranscript(r != null ? r.text : "");
                }
                catch (Exception e) { problem = "I could not read the speech result (" + e.Message + ")"; }
            }
        }
        transcribing = false;

        if (problem != null)
        {
            Debug.LogWarning("[GuideVoice] " + problem);
            Status("Sorry, " + problem);
        }
        else if (string.IsNullOrEmpty(text))
        {
            Status("I didn't catch that - could you say it again?");
        }
        else
        {
            Debug.Log("[GuideVoice] Heard: \"" + text + "\"");
            if (OnHeard != null) OnHeard(text);
        }
    }

    static readonly Regex Bracketed = new Regex(@"\[[^\]]*\]|\([^)]*\)|\*[^*]*\*");

    /// Whisper marks silence and noise as "[BLANK_AUDIO]", "(music)" and similar.
    static string CleanTranscript(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        s = Bracketed.Replace(s, " ");
        s = Regex.Replace(s, @"\s+", " ").Trim();
        return s.Trim('-', ' ', '.').Length == 0 ? "" : s;
    }

    // ------------------------------------------------------------ audio helpers

    static float[] ToMono(float[] data, int channels)
    {
        if (channels <= 1) return data;
        var mono = new float[data.Length / channels];
        for (int i = 0; i < mono.Length; i++)
        {
            float sum = 0f;
            for (int c = 0; c < channels; c++) sum += data[i * channels + c];
            mono[i] = sum / channels;
        }
        return mono;
    }

    static float[] Resample(float[] src, int fromRate, int toRate)
    {
        if (fromRate == toRate || src.Length == 0) return src;
        int n = (int)((long)src.Length * toRate / fromRate);
        var dst = new float[n];
        double step = (double)fromRate / toRate;
        for (int i = 0; i < n; i++)
        {
            double pos = i * step;
            int a = (int)pos;
            int b = Math.Min(a + 1, src.Length - 1);
            float t = (float)(pos - a);
            dst[i] = src[a] + (src[b] - src[a]) * t;
        }
        return dst;
    }

    static byte[] EncodeWav(float[] samples, int rate)
    {
        using (var ms = new MemoryStream(44 + samples.Length * 2))
        using (var w = new BinaryWriter(ms))
        {
            int dataBytes = samples.Length * 2;
            w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + dataBytes);
            w.Write(Encoding.ASCII.GetBytes("WAVE"));
            w.Write(Encoding.ASCII.GetBytes("fmt ")); w.Write(16); w.Write((short)1); w.Write((short)1);
            w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
            w.Write(Encoding.ASCII.GetBytes("data")); w.Write(dataBytes);
            foreach (var f in samples) w.Write((short)(Mathf.Clamp(f, -1f, 1f) * 32767f));
            w.Flush();
            return ms.ToArray();
        }
    }

    // ============================================================ speaking

    public void Speak(string text)
    {
        if (!speakAnswers || string.IsNullOrEmpty(text)) return;
        if (recording) return;                 // never talk into the open microphone (the answer is still on the panel)
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        StopSpeaking();
        speakRoutine = StartCoroutine(SpeakWindows(text));
#endif
    }

    public void StopSpeaking()
    {
        if (speakRoutine != null) { StopCoroutine(speakRoutine); speakRoutine = null; }
        KillTts();
        if (output != null) output.Stop();     // also a paused answer, so a later tap can't resume it
    }

    void KillTts()
    {
        try { if (ttsProcess != null && !ttsProcess.HasExited) ttsProcess.Kill(); } catch { }
        ttsProcess = null;
    }

    // Windows' own speech synthesiser, driven through a tiny PowerShell script. It writes a WAV
    // which is then played from Haku's position. Offline; ~1 s before the voice starts.
    const string TtsScript =
        "param([string]$In, [string]$Out, [int]$Rate = 0, [string]$Voice = '')\r\n" +
        "Add-Type -AssemblyName System.Speech\r\n" +
        "$s = New-Object System.Speech.Synthesis.SpeechSynthesizer\r\n" +
        "if ($Voice) { try { $s.SelectVoice($Voice) } catch { } }\r\n" +
        "else { try { $s.SelectVoiceByHints([System.Speech.Synthesis.VoiceGender]::Female) } catch { } }\r\n" +
        "$s.Rate = $Rate\r\n" +
        "$s.SetOutputToWaveFile($Out)\r\n" +
        "$s.Speak([IO.File]::ReadAllText($In))\r\n" +
        "$s.Dispose()\r\n";

    IEnumerator SpeakWindows(string text)
    {
        string dir    = Application.temporaryCachePath;
        string script = Path.Combine(dir, "haku_tts.ps1");
        string input  = Path.Combine(dir, "haku_say.txt");
        string wav    = Path.Combine(dir, "haku_say_" + (ttsCounter++ % 2) + ".wav");

        System.Threading.Tasks.Task<System.Diagnostics.Process> starting;
        try
        {
            if (!File.Exists(script) || File.ReadAllText(script) != TtsScript) File.WriteAllText(script, TtsScript);
            File.WriteAllText(input, text, Encoding.UTF8);
            if (File.Exists(wav)) File.Delete(wav);

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"" + script + "\"" +
                            " -In \"" + input + "\" -Out \"" + wav + "\" -Rate " + Mathf.Clamp(voiceRate, -10, 10) +
                            " -Voice \"" + voiceName.Replace("\"", "") + "\"",
                UseShellExecute = false,
                CreateNoWindow  = true,
            };
            // Launching powershell takes a moment: do it off the main thread so the headset doesn't hitch.
            starting = System.Threading.Tasks.Task.Run(() => System.Diagnostics.Process.Start(psi));
        }
        catch (Exception e)
        {
            Debug.LogWarning("[GuideVoice] Could not start the Windows voice: " + e.Message);
            yield break;
        }
        while (!starting.IsCompleted) yield return null;
        if (starting.IsFaulted || starting.Result == null)
        {
            Debug.LogWarning("[GuideVoice] Could not start the Windows voice: " +
                             (starting.Exception != null ? starting.Exception.GetBaseException().Message : "no process"));
            yield break;
        }
        ttsProcess = starting.Result;

        float started = Time.unscaledTime;
        while (ttsProcess != null && !ttsProcess.HasExited)
        {
            if (Time.unscaledTime - started > 30f) { KillTts(); yield break; }
            yield return null;
        }
        ttsProcess = null;

        if (!File.Exists(wav))
        {
            Debug.LogWarning("[GuideVoice] The Windows voice produced no audio.");
            yield break;
        }

        using (var www = UnityWebRequestMultimedia.GetAudioClip(new Uri(wav).AbsoluteUri, AudioType.WAV))
        {
            yield return www.SendWebRequest();
            if (www.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning("[GuideVoice] Could not load the spoken answer: " + www.error);
                yield break;
            }
            while (recording) yield return null;   // never play into an open microphone
            var clip = DownloadHandlerAudioClip.GetContent(www);
            if (output.clip != null) Destroy(output.clip);
            output.clip = clip;
            output.Play();
        }
        speakRoutine = null;
    }

    void Status(string s)
    {
        if (OnStatus != null) OnStatus(s);
    }
}

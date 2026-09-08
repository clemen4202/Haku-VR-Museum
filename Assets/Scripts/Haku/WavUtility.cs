// ---------------------------------------------------------------------------------------------
// WavUtility.cs - AudioClip <-> 16-bit PCM WAV bytes. No Unity APIs beyond AudioClip and Mathf.
//
// Encode: canonical 44-byte RIFF header, mono or stereo, signed 16-bit little-endian samples.
//         This is exactly what faster-whisper on the service accepts (any sample rate is fine -
//         it resamples internally, so 16 kHz capture is a bandwidth win, not a requirement).
//
// Decode: does NOT assume the data chunk starts at byte 44. It walks the RIFF chunk list, because
//         Piper's WAV output can carry extra chunks (LIST/fact) before the audio. Assuming offset
//         44 is the classic way to get a clip that plays a burst of noise then silence.
// ---------------------------------------------------------------------------------------------
using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace Haku
{
    public static class WavUtility
    {
        private const int HeaderBytes = 44;

        // -----------------------------------------------------------------------------------------
        // Encode
        // -----------------------------------------------------------------------------------------

        /// <summary>Whole AudioClip to a 16-bit PCM WAV byte[]. Returns null and logs on failure.</summary>
        public static byte[] FromAudioClip(AudioClip clip)
        {
            if (clip == null)
            {
                Debug.LogError("[Haku] WavUtility.FromAudioClip: clip is null.");
                return null;
            }

            if (clip.samples <= 0 || clip.channels <= 0)
            {
                Debug.LogError("[Haku] WavUtility.FromAudioClip: clip '" + clip.name + "' has " +
                               clip.samples + " samples and " + clip.channels + " channels.");
                return null;
            }

            float[] samples = new float[clip.samples * clip.channels];
            if (!clip.GetData(samples, 0))
            {
                Debug.LogError("[Haku] WavUtility.FromAudioClip: GetData failed on '" + clip.name + "'.");
                return null;
            }

            return Encode(samples, clip.channels, clip.frequency);
        }

        /// <summary>Interleaved -1..1 float samples to a 16-bit PCM WAV byte[].</summary>
        public static byte[] Encode(float[] samples, int channels, int sampleRate)
        {
            if (samples == null || samples.Length == 0)
            {
                Debug.LogError("[Haku] WavUtility.Encode: no samples.");
                return null;
            }
            if (channels <= 0 || sampleRate <= 0)
            {
                Debug.LogError("[Haku] WavUtility.Encode: bad format - channels=" + channels +
                               " sampleRate=" + sampleRate);
                return null;
            }

            int dataBytes = samples.Length * 2;

            using (MemoryStream stream = new MemoryStream(HeaderBytes + dataBytes))
            {
                // BinaryWriter writes little-endian on every platform Unity ships, which is what RIFF wants.
                using (BinaryWriter w = new BinaryWriter(stream, Encoding.ASCII, true))
                {
                    w.Write(Encoding.ASCII.GetBytes("RIFF"));        //  0  ChunkID
                    w.Write(36 + dataBytes);                          //  4  ChunkSize = 36 + data
                    w.Write(Encoding.ASCII.GetBytes("WAVE"));        //  8  Format

                    w.Write(Encoding.ASCII.GetBytes("fmt "));        // 12  Subchunk1ID
                    w.Write(16);                                      // 16  Subchunk1Size (16 = PCM)
                    w.Write((short)1);                                // 20  AudioFormat  (1 = PCM)
                    w.Write((short)channels);                         // 22  NumChannels
                    w.Write(sampleRate);                              // 24  SampleRate
                    w.Write(sampleRate * channels * 2);               // 28  ByteRate
                    w.Write((short)(channels * 2));                   // 32  BlockAlign
                    w.Write((short)16);                               // 34  BitsPerSample

                    w.Write(Encoding.ASCII.GetBytes("data"));        // 36  Subchunk2ID
                    w.Write(dataBytes);                               // 40  Subchunk2Size
                                                                      // 44  the samples
                    for (int i = 0; i < samples.Length; i++)
                    {
                        float clamped = Mathf.Clamp(samples[i], -1f, 1f);
                        w.Write((short)Mathf.RoundToInt(clamped * 32767f));
                    }

                    w.Flush();
                }

                return stream.ToArray();
            }
        }

        // -----------------------------------------------------------------------------------------
        // Decode
        // -----------------------------------------------------------------------------------------

        /// <summary>WAV bytes to a playable AudioClip. Returns null and logs on failure.</summary>
        public static AudioClip ToAudioClip(byte[] wav, string clipName)
        {
            float[] samples;
            int channels;
            int sampleRate;

            if (!TryDecode(wav, out samples, out channels, out sampleRate)) return null;

            int frames = samples.Length / channels;
            if (frames <= 0)
            {
                Debug.LogError("[Haku] WavUtility.ToAudioClip: decoded 0 frames.");
                return null;
            }

            AudioClip clip = AudioClip.Create(string.IsNullOrEmpty(clipName) ? "HakuClip" : clipName,
                                              frames, channels, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        /// <summary>
        /// Parse a 16-bit PCM WAV. Scans the chunk list for "fmt " and "data" rather than assuming
        /// offset 44. Logs precisely what was wrong on failure.
        /// </summary>
        public static bool TryDecode(byte[] wav, out float[] samples, out int channels, out int sampleRate)
        {
            samples = null;
            channels = 0;
            sampleRate = 0;

            if (wav == null || wav.Length < HeaderBytes)
            {
                Debug.LogError("[Haku] WavUtility.TryDecode: buffer is " +
                               (wav == null ? "null" : wav.Length + " bytes") + ", too short to be a WAV.");
                return false;
            }

            if (!Matches(wav, 0, "RIFF") || !Matches(wav, 8, "WAVE"))
            {
                Debug.LogError("[Haku] WavUtility.TryDecode: not a RIFF/WAVE file. First 12 bytes: " +
                               SafeAscii(wav, 0, 12) + " (an HTML or JSON error page from the service " +
                               "shows up here - read the response body).");
                return false;
            }

            int audioFormat = 0;
            int bitsPerSample = 0;
            int dataOffset = -1;
            int dataLength = 0;

            int pos = 12;
            while (pos + 8 <= wav.Length)
            {
                string chunkId = Encoding.ASCII.GetString(wav, pos, 4);
                int chunkSize = BitConverter.ToInt32(wav, pos + 4);
                int body = pos + 8;

                // Tolerate a truncated tail rather than throwing.
                if (chunkSize < 0 || body + chunkSize > wav.Length) chunkSize = wav.Length - body;

                if (chunkId == "fmt " && chunkSize >= 16)
                {
                    audioFormat = BitConverter.ToInt16(wav, body);
                    channels = BitConverter.ToInt16(wav, body + 2);
                    sampleRate = BitConverter.ToInt32(wav, body + 4);
                    bitsPerSample = BitConverter.ToInt16(wav, body + 14);
                }
                else if (chunkId == "data")
                {
                    dataOffset = body;
                    dataLength = chunkSize;
                }

                pos = body + chunkSize + (chunkSize % 2);   // RIFF chunks are word aligned
            }

            // 1 = PCM, 65534 = WAVE_FORMAT_EXTENSIBLE (still linear PCM when bits == 16).
            if (audioFormat != 1 && audioFormat != 65534)
            {
                Debug.LogError("[Haku] WavUtility.TryDecode: unsupported audio format " + audioFormat +
                               " (only uncompressed PCM is handled).");
                return false;
            }
            if (bitsPerSample != 16)
            {
                Debug.LogError("[Haku] WavUtility.TryDecode: " + bitsPerSample +
                               "-bit audio, expected 16-bit PCM.");
                return false;
            }
            if (channels <= 0 || sampleRate <= 0)
            {
                Debug.LogError("[Haku] WavUtility.TryDecode: no usable 'fmt ' chunk (channels=" +
                               channels + ", sampleRate=" + sampleRate + ").");
                return false;
            }
            if (dataOffset < 0 || dataLength < 2)
            {
                Debug.LogError("[Haku] WavUtility.TryDecode: no 'data' chunk in " + wav.Length + " bytes.");
                return false;
            }

            int sampleCount = dataLength / 2;
            sampleCount -= sampleCount % channels;          // never leave a half frame

            samples = new float[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                int b = dataOffset + i * 2;
                short value = (short)(wav[b] | (wav[b + 1] << 8));
                samples[i] = value / 32768f;
            }

            return true;
        }

        private static bool Matches(byte[] buffer, int offset, string ascii)
        {
            if (offset + ascii.Length > buffer.Length) return false;
            for (int i = 0; i < ascii.Length; i++)
                if (buffer[offset + i] != (byte)ascii[i]) return false;
            return true;
        }

        private static string SafeAscii(byte[] buffer, int offset, int count)
        {
            int available = Mathf.Min(count, buffer.Length - offset);
            if (available <= 0) return "";
            StringBuilder sb = new StringBuilder(available);
            for (int i = 0; i < available; i++)
            {
                byte b = buffer[offset + i];
                sb.Append(b >= 32 && b < 127 ? (char)b : '.');
            }
            return sb.ToString();
        }
    }
}

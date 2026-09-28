using System.Runtime.InteropServices;
using UnityEngine;

namespace StoryMint.RiftHunt
{
    /// <summary>
    /// Thin static wrapper around the WebGL Web-Audio sound effects.
    /// In the Editor, plays simple AudioClip beeps via a helper AudioSource.
    /// Call ResumeContext() once after a user gesture (tap) to unlock audio on mobile browsers.
    /// </summary>
    public static class StoryMintSFX
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern void StoryMintAudio_ResumeContext();
        [DllImport("__Internal")] private static extern void StoryMintAudio_Tap();
        [DllImport("__Internal")] private static extern void StoryMintAudio_Success();
        [DllImport("__Internal")] private static extern void StoryMintAudio_Error();
        [DllImport("__Internal")] private static extern void StoryMintAudio_Victory();
        [DllImport("__Internal")] private static extern void StoryMintAudio_Tick();
        [DllImport("__Internal")] private static extern void StoryMintAudio_SimonBlink(int padIndex);
        [DllImport("__Internal")] private static extern void StoryMintAudio_Catch();
        [DllImport("__Internal")] private static extern void StoryMintAudio_Fall();
        [DllImport("__Internal")] private static extern void StoryMintAudio_TraceDot();
        [DllImport("__Internal")] private static extern void StoryMintAudio_Penalty();
        [DllImport("__Internal")] private static extern void StoryMintAudio_PopUp();
        [DllImport("__Internal")] private static extern void StoryMintAudio_Reset();
#else
        // ---- Editor stubs — play procedural AudioClips ----
        private static AudioSource _editorSource;

        private static AudioSource GetEditorSource()
        {
            if (_editorSource != null) return _editorSource;
            var go = new GameObject("[SFX-EditorPreview]");
            go.hideFlags = HideFlags.HideAndDontSave;
            Object.DontDestroyOnLoad(go);
            _editorSource = go.AddComponent<AudioSource>();
            _editorSource.playOnAwake = false;
            _editorSource.volume = 0.35f;
            return _editorSource;
        }

        private static AudioClip MakeTone(float freq, float duration, float volume = 0.5f)
        {
            int sampleRate = 44100;
            int samples = Mathf.CeilToInt(sampleRate * duration);
            AudioClip clip = AudioClip.Create("sfx", samples, 1, sampleRate, false);
            float[] data = new float[samples];
            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / sampleRate;
                float envelope = Mathf.Clamp01(1f - t / duration);
                data[i] = Mathf.Sin(2f * Mathf.PI * freq * t) * volume * envelope;
            }
            clip.SetData(data, 0);
            return clip;
        }

        private static void PlayTone(float freq, float dur, float vol = 0.4f)
        {
            var src = GetEditorSource();
            src.PlayOneShot(MakeTone(freq, dur, vol));
        }

        private static void StoryMintAudio_ResumeContext() { }
        private static void StoryMintAudio_Tap() { PlayTone(800, 0.08f, 0.3f); }
        private static void StoryMintAudio_Success() { PlayTone(660, 0.15f, 0.35f); }
        private static void StoryMintAudio_Error() { PlayTone(180, 0.25f, 0.2f); }
        private static void StoryMintAudio_Victory() { PlayTone(784, 0.4f, 0.3f); }
        private static void StoryMintAudio_Tick() { PlayTone(1000, 0.04f, 0.15f); }
        private static void StoryMintAudio_SimonBlink(int padIndex)
        {
            float[] pitches = { 261, 329, 392, 523 };
            PlayTone(pitches[padIndex % pitches.Length], 0.3f, 0.4f);
        }
        private static void StoryMintAudio_Catch() { PlayTone(880, 0.12f, 0.3f); }
        private static void StoryMintAudio_Fall() { PlayTone(200, 0.3f, 0.25f); }
        private static void StoryMintAudio_TraceDot() { PlayTone(700, 0.06f, 0.2f); }
        private static void StoryMintAudio_Penalty() { PlayTone(150, 0.2f, 0.18f); }
        private static void StoryMintAudio_PopUp() { PlayTone(500, 0.06f, 0.15f); }
        private static void StoryMintAudio_Reset() { PlayTone(200, 0.25f, 0.2f); }
#endif

        // ============================================================
        //  Public API — call these from game scripts
        // ============================================================

        /// <summary>Call once after a user gesture to unlock Web Audio on iOS/Android browsers.</summary>
        public static void ResumeContext() => StoryMintAudio_ResumeContext();

        /// <summary>Generic UI tap sound.</summary>
        public static void Tap() => StoryMintAudio_Tap();

        /// <summary>Correct action — rising two-tone.</summary>
        public static void Success() => StoryMintAudio_Success();

        /// <summary>Wrong action — low buzz.</summary>
        public static void Error() => StoryMintAudio_Error();

        /// <summary>Game won — ascending fanfare.</summary>
        public static void Victory() => StoryMintAudio_Victory();

        /// <summary>Timer tick.</summary>
        public static void Tick() => StoryMintAudio_Tick();

        /// <summary>Simon Says pad flash — pitch mapped to pad index 0-3.</summary>
        public static void SimonBlink(int padIndex) => StoryMintAudio_SimonBlink(padIndex);

        /// <summary>Rune caught (RuneCatcher).</summary>
        public static void Catch() => StoryMintAudio_Catch();

        /// <summary>Ball fell in hole (GyroMaze).</summary>
        public static void Fall() => StoryMintAudio_Fall();

        /// <summary>Tracing dot feedback (SealTrace).</summary>
        public static void TraceDot() => StoryMintAudio_TraceDot();

        /// <summary>Penalty buzz (WhackARune wrong tap).</summary>
        public static void Penalty() => StoryMintAudio_Penalty();

        /// <summary>Rune pop-up appear (WhackARune).</summary>
        public static void PopUp() => StoryMintAudio_PopUp();

        /// <summary>Sequence reset / seal broken.</summary>
        public static void Reset() => StoryMintAudio_Reset();
    }
}

using System.Collections;
using System.Collections.Generic;
using LBoL.ConfigData;
using LBoL.Core;
using LBoL.Presentation;
using LBoL.Presentation.Effect;
using LBoL.Presentation.Units;
using UnityEngine;

namespace LBOLMP.UI
{
    /// <summary>
    /// Plays a unit's death backwards, for the Defibrillator tool card.
    /// </summary>
    /// This was a really stupid idea but it's kind of funny.
    internal static class MpRevivalFx
    {
        /// <summary>The names and lengths <c>DieRunner</c> picks from, by the unit's die level.</summary>
        private static void Names(UnitView view, out string effect, out string sfx, out float seconds)
        {
            switch (view._dieLevel)
            {
                case 0:
                    effect = "UnitDeathSmall";
                    sfx = "UnitDeathExplodeSmall";
                    seconds = 0.5f;
                    return;
                case 2:
                    effect = "UnitDeathLarge";
                    sfx = "UnitDeathExplodeLarge";
                    seconds = 1.8f;
                    return;
                default:
                    effect = "UnitDeath";
                    sfx = "UnitDeathExplode";
                    seconds = 1f;
                    return;
            }
        }

        /// <summary>Ally unit views that are currently already being revived.</summary>
        private static readonly HashSet<UnitView> Running = new HashSet<UnitView>();

        internal static bool InProgress(UnitView view) => view != null && Running.Contains(view);

        internal static void Play(UnitView view)
        {
            if (view == null || MpPlugin.Instance == null || Running.Contains(view))
            {
                return;
            }

            Running.Add(view);
            MpPlugin.Instance.StartCoroutine(Run(view));
        }

        private static IEnumerator Run(UnitView view)
        {
            string effectName = "UnitDeath";
            string sfxName = "UnitDeathExplode";
            float seconds = 1f;
            MpSafe.Run("MpRevivalFx.Names", () => Names(view, out effectName, out sfxName, out seconds));

            // This whole thing was an incredibly dumb idea but I *am* keeping it
            try
            {
                // Spawn death particle FX
                var widget = MpSafe.Run("MpRevivalFx.Effect",
                    () => EffectManager.CreateEffect(effectName, view.transform, 0f, null, false, true),
                    null);

                // Play the death sound in reverse
                MpSafe.Run("MpRevivalFx.EffectSfx", () => ReverseSources(widget));
                float sound = MpSafe.Run("MpRevivalFx.Sfx", () => Bang(sfxName, seconds), 0f);

                // Play the character flinch animation slowly, backwards
                float scale = Mathf.Max(0.01f, Time.timeScale);
                float hold = Mathf.Clamp(sound - seconds / scale, 0f, MaxHoldSeconds);
                float tail = seconds * (1f - BloomFraction) + hold * scale;

                // Wait a little bit for the particles before we unhide the unit
                yield return new WaitForSeconds(seconds * BloomFraction);

                MpSafe.Run("MpRevivalFx.Undie", () => MpAllyUnits.Undie(view));
                MpSafe.Run("MpRevivalFx.Flinch", () => Unflinch(view, tail));

                yield return new WaitForSeconds(tail);

                if (widget != null)
                {
                    Object.Destroy(widget.gameObject);
                }

                // And, we're back! Play the idle animation again.
                MpSafe.Run("MpRevivalFx.Idle", () => view.SpineIdle(false));
            }
            finally
            {
                Running.Remove(view);
            }
        }

        /// <summary>How much of the explosion happens before the character is unhidden.</summary>
        private const float BloomFraction = 0.45f;

        private const float FlinchSpeed = 0.1f;

        private static void Unflinch(UnitView view, float seconds)
        {
            if (!view.SpineLoaded || !view.AllAnimationsNames.Contains("hit"))
            {
                view.SpineIdle(false);
                return;
            }

            foreach (var state in view.AllStates)
            {
                var entry = state.SetAnimation(0, "hit", false);
                entry.TrackTime = Mathf.Min(entry.Animation.Duration, FlinchSpeed * seconds);
                entry.TimeScale = -FlinchSpeed;
            }
        }

        // Reverse the audio sources because funny
        private static void ReverseSources(Component widget)
        {
            if (widget == null)
            {
                return;
            }

            foreach (var source in widget.GetComponentsInChildren<AudioSource>(true))
            {
                if (source.clip == null)
                {
                    continue;
                }

                // Play the audio backwards, making sure to seek towards the end of the clip
                source.pitch = -1f;
                source.timeSamples = Mathf.Max(0, source.clip.samples - 1);

                if (!source.isPlaying)
                {
                    source.Play();
                }
            }
        }

        private static float Bang(string sfxName, float lifetime)
        {
            var config = SfxConfig.FromName(sfxName);
            var clip = config == null ? null : MpSafe.Run("MpRevivalFx.Clip", () => LoadClip(config), null);

            if (clip == null)
            {
                // TODO: Is this still necessary?
                MpPlugin.Log.LogWarning($"Failed to play '{sfxName}', playing the death sound normally instead");
                AudioManager.PlaySfx(sfxName);
                return 0f;
            }

            float onScreen = lifetime / Mathf.Max(0.01f, Time.timeScale);

            var backwards = MpSafe.Run("MpRevivalFx.Reverse", () => Reversed(clip, onScreen), null);

            var holder = new GameObject("MpRevivalSfx");
            var source = holder.AddComponent<AudioSource>();
            source.clip = backwards ?? clip;

            source.volume = config.Volume;
            source.outputAudioMixerGroup = MpSafe.Run("MpRevivalFx.Mixer",
                () => Singleton<AudioManager>.Instance._sfxGroup, null);
            source.spatialBlend = 0f;
            source.Play();

            float life = source.clip.length + 0.5f;
            Object.Destroy(holder, life);

            if (backwards != null)
            {
                Object.Destroy(backwards, life);
            }

            return backwards == null ? 0f : backwards.length;
        }

        /// <summary>
        /// Get the first couple of starting seconds of the audio clip.
        /// </summary>
        /// The death SFX is kind of long and drawn-out, we only need a part of it.
        private static AudioClip Reversed(AudioClip clip, float seconds)
        {
            if (clip.loadState != AudioDataLoadState.Loaded && !clip.LoadAudioData())
            {
                return null;
            }

            int channels = clip.channels;
            var samples = new float[clip.samples * channels];
            if (!clip.GetData(samples, 0))
            {
                return null;
            }

            int frames = Buildup(samples, channels, clip.frequency, seconds, clip.samples);
            var reversed = new float[frames * channels];

            // Weird magic to reverse the audio clip sample by sample because I was too lazy to just take the original SFX and bundle it with the mod
            // Is this not too much for a multiplayer mod?
            for (int frame = 0; frame < frames; frame++)
            {
                int from = frames - 1 - frame;
                for (int channel = 0; channel < channels; channel++)
                {
                    reversed[frame * channels + channel] = samples[from * channels + channel];
                }
            }

            Fade(reversed, channels, clip.frequency / 100, true);
            Fade(reversed, channels, clip.frequency / 200, false);

            var built = AudioClip.Create(clip.name + "Reversed", frames, channels, clip.frequency, false);

            return built.SetData(reversed, 0) ? built : null;
        }


        private const float BuildupLevel = 0.5f;
        private const float BuildupLeadSeconds = 1f;
        private const float MaxBuildupSeconds = 3.5f;
        private const float MaxHoldSeconds = 2f;

        private static int Buildup(float[] samples, int channels, int frequency, float floor, int total)
        {
            float peak = 0f;
            foreach (var sample in samples)
            {
                peak = Mathf.Max(peak, Mathf.Abs(sample));
            }

            int frames = total;
            float level = peak * BuildupLevel;

            for (int frame = total - 1; frame >= 0; frame--)
            {
                bool loud = false;
                for (int channel = 0; channel < channels && !loud; channel++)
                {
                    loud = Mathf.Abs(samples[frame * channels + channel]) >= level;
                }

                if (loud)
                {
                    frames = frame + 1;
                    break;
                }
            }

            frames += Mathf.RoundToInt(BuildupLeadSeconds * frequency);

            return Mathf.Clamp(frames,
                Mathf.RoundToInt(floor * frequency),
                Mathf.Min(total, Mathf.RoundToInt(MaxBuildupSeconds * frequency)));
        }

        private static void Fade(float[] samples, int channels, int length, bool atStart)
        {
            int frames = samples.Length / channels;
            length = Mathf.Clamp(length, 0, frames);

            for (int step = 0; step < length; step++)
            {
                float gain = step / (float)length;
                int frame = atStart ? step : frames - 1 - step;

                for (int channel = 0; channel < channels; channel++)
                {
                    samples[frame * channels + channel] *= gain;
                }
            }
        }

        private static AudioClip LoadClip(SfxConfig config)
        {
            string path = config.Folder + "/" + config.Path;

            int open = path.IndexOf('{');
            int close = path.IndexOf('}');
            int dash = open < 0 ? -1 : path.IndexOf('-', open + 1);

            if (open < 0 || close < open || dash < 0 || dash > close)
            {
                return ResourcesHelper.LoadSfx(path);
            }

            string first = path.Substring(open + 1, dash - open - 1);
            string last = path.Substring(dash + 1, close - dash - 1);

            if (!int.TryParse(first, out int lo) || !int.TryParse(last, out int hi) || lo >= hi)
            {
                return ResourcesHelper.LoadSfx(path);
            }

            int pick = Random.Range(lo, hi + 1);
            string numbered = path.Substring(0, open)
                              + pick.ToString().PadLeft(first.Length, '0')
                              + path.Substring(close + 1);

            return ResourcesHelper.LoadSfx(numbered);
        }
    }
}

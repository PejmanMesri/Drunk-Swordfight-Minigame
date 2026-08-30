using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// All the minigame's sounds, synthesized at runtime — no audio assets, no
/// licenses, no downloads. Whoosh, clang, thud, glug, countdown beeps and a
/// slightly too-pleased-with-itself victory fanfare.
/// </summary>
public static class ProceduralAudio
{
    private const int Rate = 44100;
    private static readonly Dictionary<string, AudioClip> Cache = new();

    public static AudioClip Whoosh() => Get("whoosh", 0.30f, t =>
    {
        float env = Mathf.Sin(Mathf.PI * t);                          // smooth in/out
        float cutoff = 0.08f + 0.5f * Mathf.Sin(Mathf.PI * t);        // filter opens mid-swing
        return LowPassedNoise(t, cutoff) * env * 0.8f;
    });

    public static AudioClip Clang() => Get("clang", 0.45f, t =>
    {
        float decay = Mathf.Exp(-t * 9f);
        float v = 0f;
        v += Mathf.Sin(2f * Mathf.PI * 420f * t) * 0.5f;
        v += Mathf.Sin(2f * Mathf.PI * 420f * 2.76f * t) * 0.3f;
        v += Mathf.Sin(2f * Mathf.PI * 420f * 5.40f * t) * 0.2f;
        v += Mathf.Sin(2f * Mathf.PI * 420f * 8.93f * t) * 0.12f;
        return v * decay * 0.6f;
    });

    public static AudioClip Thud() => Get("thud", 0.28f, t =>
    {
        float decay = Mathf.Exp(-t * 16f);
        float freq = Mathf.Lerp(95f, 55f, t / 0.28f);
        return (Mathf.Sin(2f * Mathf.PI * freq * t) * 0.9f + LowPassedNoise(t, 0.02f) * 0.4f) * decay;
    });

    public static AudioClip Glug() => Get("glug", 0.55f, t =>
    {
        float v = 0f;
        // three descending gulps, then a satisfied "ahh" low blip
        v += Blip(t, 0.00f, 0.09f, 330f);
        v += Blip(t, 0.16f, 0.09f, 265f);
        v += Blip(t, 0.32f, 0.09f, 210f);
        v += Blip(t, 0.46f, 0.08f, 150f) * 0.8f;
        return v * 0.7f;
    });

    public static AudioClip Beep(float freq) => Get($"beep{freq:0}", 0.14f, t =>
    {
        float env = Mathf.Clamp01(t / 0.01f) * Mathf.Exp(-t * 22f);
        return Mathf.Sin(2f * Mathf.PI * freq * t) * env * 0.8f;
    });

    public static AudioClip Fanfare() => Get("fanfare", 1.0f, t =>
    {
        float v = 0f;
        v += Note(t, 0.00f, 0.16f, 523.25f);   // C5
        v += Note(t, 0.18f, 0.16f, 659.25f);   // E5
        v += Note(t, 0.36f, 0.16f, 783.99f);   // G5
        v += Note(t, 0.54f, 0.40f, 1046.5f);   // C6, hold
        return v * 0.5f;
    });

    // ---------------------------------------------------------------- synth helpers

    private static float Blip(float t, float start, float len, float freq)
    {
        if (t < start || t > start + len) return 0f;
        float lt = (t - start) / len;
        float env = Mathf.Sin(Mathf.PI * lt);
        return Mathf.Sin(2f * Mathf.PI * freq * (1f - 0.25f * lt) * (t - start)) * env;
    }

    private static float Note(float t, float start, float len, float freq)
    {
        if (t < start || t > start + len) return 0f;
        float lt = (t - start) / len;
        float env = Mathf.Clamp01(lt / 0.1f) * Mathf.Exp(-lt * 2.2f);
        float v = Mathf.Sin(2f * Mathf.PI * freq * (t - start));
        v += 0.35f * Mathf.Sin(2f * Mathf.PI * freq * 2f * (t - start));   // brightness
        return v * env;
    }

    private static float _noiseState;
    private static float LowPassedNoise(float t, float alpha)
    {
        // deterministic pseudo-noise through a one-pole filter
        _noiseState = Mathf.Lerp(_noiseState, Hash01((int)(t * Rate)), alpha);
        return (_noiseState - 0.5f) * 2f;
    }

    private static float Hash01(int n)
    {
        n = (n << 13) ^ n;
        return ((n * (n * n * 15731 + 789221) + 1376312589) & 0x7fffffff) / 1073741824f * 0.5f;
    }

    private static AudioClip Get(string key, float seconds, System.Func<float, float> sample)
    {
        if (Cache.TryGetValue(key, out var cached)) return cached;

        int count = Mathf.CeilToInt(seconds * Rate);
        var data = new float[count];
        for (int i = 0; i < count; i++)
            data[i] = Mathf.Clamp(sample((float)i / Rate), -1f, 1f);

        var clip = AudioClip.Create(key, count, 1, Rate, false);
        clip.SetData(data, 0);
        Cache[key] = clip;
        return clip;
    }
}

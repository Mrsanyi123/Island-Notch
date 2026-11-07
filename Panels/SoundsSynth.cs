using System;
using System.IO;
namespace Island;

/// <summary>
/// Procedural ambience: renders loopable 16-bit mono 44.1 kHz WAV files from noise and simple oscillators.
/// These are synthesised approximations, not recordings. The last 2 s are crossfaded into the start so the loop has no seam.
/// </summary>
internal static class SoundsSynth
{
    private const int Rate = 44100;
    private const float Tau = MathF.PI * 2;

    /// <summary>Render sound `key` (rain, forest, coffeeshop, office, lofi, fireworks, whispers, waves) to a WAV file. Throws on I/O errors (caller catches).</summary>
    public static void Generate(string key, string path)
    {
        double seconds = key == "lofi" ? 32 : 36;                         // lo-fi: 4 chords x 8 s so the progression loops exactly
        int n = (int)(seconds * Rate), fade = 2 * Rate; var g = new float[n + fade];
        var r = new Rng(0x9E3779B9u ^ (uint)key.GetHashCode() * 2654435761u);
        switch (key)
        {
            case "rain": Rain(g, r); break;
            case "waves": Waves(g, r, seconds); break;
            case "forest": Forest(g, r, seconds); break;
            case "coffeeshop": Coffee(g, r); break;
            case "office": Office(g, r); break;
            case "lofi": LoFi(g, r); break;
            case "fireworks": Fireworks(g, r); break;
            default: Whispers(g, r, seconds); break;
        }
        // seamless loop: equal-power crossfade of the tail (g[n..]) into the head
        var o = new float[n];
        for (int i = 0; i < n; i++)
        {
            if (i < fade) { float w = i / (float)fade; o[i] = g[i] * MathF.Sqrt(w) + g[n + i] * MathF.Sqrt(1 - w); } else o[i] = g[i];
        }
        double peak = 1e-6, sum = 0; foreach (var s in o) { peak = Math.Max(peak, Math.Abs(s)); sum += s * s; }
        double rms = Math.Sqrt(sum / n) + 1e-9, gain = Math.Min(0.9 / peak, 0.15 / rms);
        WriteWav(path, o, (float)gain);
    }

    private static void WriteWav(string path, float[] data, float gain)
    {
        var tmp = path + ".tmp";
        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write)) using (var w = new BinaryWriter(fs))
        {
            int bytes = data.Length * 2;
            w.Write(new[] { 'R', 'I', 'F', 'F' }); w.Write(36 + bytes); w.Write(new[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' }); w.Write(16); w.Write((short)1); w.Write((short)1);
            w.Write(Rate); w.Write(Rate * 2); w.Write((short)2); w.Write((short)16); w.Write(new[] { 'd', 'a', 't', 'a' }); w.Write(bytes);
            foreach (var s in data) w.Write((short)Math.Clamp(s * gain * 32767f, -32767f, 32767f));
        }
        File.Move(tmp, path, true);
    }

    // ---------- building blocks ----------
    private sealed class Rng
    {
        private uint s; public Rng(uint seed) { s = seed == 0 ? 1 : seed; }
        private uint Next() { s ^= s << 13; s ^= s >> 17; s ^= s << 5; return s; }
        public float F() => Next() / 2147483648f - 1f;                  // -1..1
        public float U() => Next() / 4294967296f;                      // 0..1
        public float Between(float a, float b) => a + (b - a) * U();
    }
    private sealed class Lp
    {
        private readonly float a; private float y;
        public Lp(float fc) { a = 1 - MathF.Exp(-Tau * fc / Rate); }
        public float Do(float x) { y += a * (x - y); return y; }
    }
    /// <summary>Band-pass biquad (constant peak gain), retunable.</summary>
    private sealed class Bp
    {
        private float b0, a1, a2, x1, x2, y1, y2;
        public Bp(float f, float q) { Set(f, q); }
        public void Set(float f, float q)
        {
            float w = Tau * Math.Clamp(f, 20, 20000) / Rate, al = MathF.Sin(w) / (2 * q), a0 = 1 + al; b0 = al / a0; a1 = -2 * MathF.Cos(w) / a0; a2 = (1 - al) / a0;
        }
        public float Do(float x) { float y = b0 * x - b0 * x2 - a1 * y1 - a2 * y2; x2 = x1; x1 = x; y2 = y1; y1 = y; return y; }
    }
    private static float Lfo(int i, double seconds, int k, float phase = 0) => MathF.Sin((float)(Tau * k * i / (seconds * Rate)) + phase);

    /// <summary>Short decaying sine ping (droplet, clink, click).</summary>
    private static void Ping(float[] g, int pos, float amp, float freq, float decay, float partial = 0)
    {
        int len = (int)(decay * Rate * 6); float ph = 0, inc = Tau * freq / Rate, k = 1f / (decay * Rate);
        for (int j = 0; j < len && pos + j < g.Length; j++) { if (pos + j < 0) continue; float e = MathF.Exp(-j * k); g[pos + j] += (MathF.Sin(ph) + partial * MathF.Sin(ph * 2.7f)) * amp * e; ph += inc; }
    }
    private static void Burst(float[] g, int pos, float amp, float decay, Rng r, Lp? shape = null)
    {
        int len = (int)(decay * Rate * 6); float k = 1f / (decay * Rate);
        for (int j = 0; j < len && pos + j < g.Length; j++) { if (pos + j < 0) continue; float w = r.F(); if (shape != null) w = shape.Do(w) * 2.5f; g[pos + j] += w * amp * MathF.Exp(-j * k); }
    }
    private static float Midi(int note) => 440f * MathF.Pow(2, (note - 69) / 12f);

    // ---------- the eight rooms ----------
    private static void Rain(float[] g, Rng r)
    {
        var hi = new Lp(9000); var lo = new Lp(1100);
        for (int i = 0; i < g.Length; i++) { float w = r.F(); g[i] = (hi.Do(w) - lo.Do(w)) * 0.55f * (0.9f + 0.1f * Lfo(i, 36, 7)); }
        int drops = (int)(g.Length / (float)Rate * 150);
        for (int d = 0; d < drops; d++) Ping(g, (int)(r.U() * g.Length), r.Between(.03f, .22f), r.Between(1800, 6800), r.Between(.002f, .008f));
        for (int d = 0; d < drops / 8; d++) Ping(g, (int)(r.U() * g.Length), r.Between(.05f, .15f), r.Between(350, 900), r.Between(.01f, .03f));
    }

    private static void Waves(float[] g, Rng r, double sec)
    {
        var brown = new Lp(500); var foam = new Lp(4500); var foamLo = new Lp(1500); float b = 0;
        for (int i = 0; i < g.Length; i++)
        {
            float w = r.F(); b = (b + w * 0.03f) * 0.998f;
            float s1 = 0.5f + 0.5f * Lfo(i, sec, 4), s2 = 0.5f + 0.5f * Lfo(i, sec, 5, 1.3f), env = 0.18f + 0.82f * MathF.Pow((s1 + s2) * 0.5f, 1.6f);
            g[i] = brown.Do(b * 6f) * env * 1.4f + (foam.Do(w) - foamLo.Do(w)) * env * env * 0.5f;
        }
    }

    private static void Forest(float[] g, Rng r, double sec)
    {
        var wind = new Lp(380); var leaf = new Lp(2200); var leafLo = new Lp(700);
        for (int i = 0; i < g.Length; i++) { float w = r.F(), m = 0.55f + 0.45f * Lfo(i, sec, 3, 0.7f); g[i] = wind.Do(w) * 1.6f * m + (leaf.Do(w) - leafLo.Do(w)) * 0.22f * m * m; }
        // sparse birds: 1-4 short FM chirps each
        for (float t = r.Between(.4f, 1.5f); t < sec + 2; t += r.Between(1.6f, 4.2f))
        {
            int notes = 1 + (int)(r.U() * 4); float f0 = r.Between(2400, 4600), gap = r.Between(.09f, .18f), sweep = r.Between(-1800, 2600);
            for (int k = 0; k < notes; k++)
            {
                int pos = (int)((t + k * gap) * Rate), len = (int)(r.Between(.05f, .1f) * Rate); float ph = 0, mph = 0, amp = r.Between(.12f, .24f) * (1 - k * .1f);
                for (int j = 0; j < len && pos + j < g.Length; j++)
                {
                    float u = j / (float)len, env = MathF.Pow(MathF.Sin(MathF.PI * u), 2), f = f0 + sweep * u + 300 * MathF.Sin(mph);
                    ph += Tau * f / Rate; mph += Tau * 38 / Rate; g[pos + j] += MathF.Sin(ph) * env * amp;
                }
            }
        }
    }

    private static void Coffee(float[] g, Rng r)
    {
        var centres = new[] { 330f, 500f, 720f, 1050f }; var bp = new Bp[4]; var env = new float[4]; var target = new float[4]; var hold = new int[4];
        for (int v = 0; v < 4; v++) bp[v] = new Bp(centres[v], 1.3f);
        float sm = 1 - MathF.Exp(-1f / (0.045f * Rate)); var rumble = new Lp(220);
        for (int i = 0; i < g.Length; i++)
        {
            float w = r.F(), sum = 0;
            for (int v = 0; v < 4; v++)
            {
                if (--hold[v] <= 0) { hold[v] = (int)(r.Between(.1f, .38f) * Rate); target[v] = r.U() < .35f ? r.U() * .08f : r.U(); }
                env[v] += (target[v] - env[v]) * sm; sum += bp[v].Do(w) * env[v];
            }
            g[i] = sum * 0.55f + rumble.Do(w) * 0.25f;
        }
        for (float t = r.Between(1, 3); t < g.Length / (float)Rate; t += r.Between(3.5f, 7f))
        {
            int pos = (int)(t * Rate); float a = r.Between(.08f, .14f);
            Ping(g, pos, a, 3300, .045f, .5f); Ping(g, pos, a * .7f, 4800, .03f); Ping(g, pos + (int)(.07f * Rate), a * .6f, 3900, .04f, .4f);
        }
    }

    private static void Office(float[] g, Rng r)
    {
        var a = new Lp(450); var air = new Lp(3200); var airLo = new Lp(300);
        for (int i = 0; i < g.Length; i++)
        {
            float w = r.F(), t = i / (float)Rate;
            g[i] = a.Do(w) * 0.9f + (air.Do(w) - airLo.Do(w)) * 0.18f + MathF.Sin(Tau * 100 * t) * 0.012f + MathF.Sin(Tau * 200.4f * t) * 0.006f;
        }
        var shape = new Lp(5000);
        for (float t = r.Between(1, 3); t < g.Length / (float)Rate; t += r.Between(2.5f, 7f))
        {
            int clicks = 3 + (int)(r.U() * 14); float tt = t;
            for (int c = 0; c < clicks; c++) { tt += r.Between(.07f, .22f); int pos = (int)(tt * Rate); float amp = r.Between(.05f, .12f); Burst(g, pos, amp, .0012f, r, shape); Ping(g, pos, amp * .5f, r.Between(1400, 2400), .004f); }
        }
    }

    private static void LoFi(float[] g, Rng r)
    {
        // Cmaj7 - Am7 - Fmaj7 - G7, 8 s each, 75 bpm (beat 0.8 s, eighth 0.4 s)
        int[][] chords = { new[] { 48, 55, 59, 64 }, new[] { 45, 55, 60, 64 }, new[] { 41, 57, 60, 64 }, new[] { 43, 59, 62, 65 } };
        for (int c = 0; c < 5; c++)
        {
            var ch = chords[c % 4]; int start = (int)((c * 8 - 1.5) * Rate), len = (int)(11 * Rate);
            for (int v = 1; v < 4; v++) for (int d = -1; d <= 1; d += 2)
            {
                float f = Midi(ch[v]) * (1 + d * .0035f), ph = r.U() * Tau, inc = Tau * f / Rate;
                for (int j = 0; j < len; j++)
                {
                    int idx = start + j; if (idx < 0) { ph += inc; continue; } if (idx >= g.Length) break;
                    float t = j / (float)Rate, e = Math.Min(Math.Min(t / 1.6f, 1), Math.Min((11 - t) / 1.6f, 1)); e = e * e * (3 - 2 * e);
                    float tri = 2 / MathF.PI * MathF.Asin(MathF.Sin(ph)); g[idx] += (MathF.Sin(ph) * .6f + tri * .25f + MathF.Sin(ph * 2) * .1f) * e * 0.05f; ph += inc;
                }
            }
            for (int h = 0; h < 2; h++) Ping(g, (int)((c * 8 + h * 4) * Rate), .13f, Midi(ch[0]), 1.2f, 0);      // bass
        }
        int[] pat = { 0, 1, 2, 1, 2, 0, 1, 2, 1, 0, 2, 1, 0, 1, 2, 1, 2, 1, 0, 1 };
        int eighths = (int)((g.Length / (float)Rate) / 0.4f) + 1;
        for (int e = 0; e < eighths; e++)
        {
            var ch = chords[e / 20 % 4]; float f = Midi(ch[1 + pat[e % 20]] + 12), t = e * 0.4f + (e % 2 == 1 ? .035f : 0); int pos = (int)(t * Rate);
            Ping(g, pos, .085f, f, .28f, 0); Ping(g, pos, .05f, f * 1.0045f, .22f, 0);
        }
        for (float t = 0; t < g.Length / (float)Rate; t += 1.6f)
        {
            int p = (int)(t * Rate), len = (int)(.2f * Rate); float ph = 0;                              // soft kick
            for (int j = 0; j < len && p + j < g.Length; j++) { float u = j / (float)Rate; ph += Tau * (48 + 90 * MathF.Exp(-u * 28)) / Rate; g[p + j] += MathF.Sin(ph) * MathF.Exp(-u * 16) * .3f; }
            Burst(g, p + (int)(.8f * Rate), .05f, .05f, r, new Lp(3000));                                // snare-ish
        }
        var hat = new Lp(9000); for (float t = .4f; t < g.Length / (float)Rate; t += .8f) Burst(g, (int)(t * Rate), .03f, .008f, r, hat);
        var tapeA = new Lp(4200); var tapeB = new Lp(4200); var hiss = new Lp(6000);
        for (int i = 0; i < g.Length; i++) g[i] = tapeB.Do(tapeA.Do(g[i])) + hiss.Do(r.F()) * .01f;
        int pops = (int)(g.Length / (float)Rate * 14); for (int p = 0; p < pops; p++) Ping(g, (int)(r.U() * g.Length), r.Between(.01f, .06f), r.Between(2500, 6000), .0015f);
    }

    private static void Fireworks(float[] g, Rng r)
    {
        var bed = new Lp(260); for (int i = 0; i < g.Length; i++) g[i] = bed.Do(r.F()) * .05f;
        var rumble = new Lp(200);
        for (float t = r.Between(.5f, 2); t < g.Length / (float)Rate; t += r.Between(3.2f, 7f))
        {
            for (int echo = 0; echo < 2; echo++)
            {
                int p = (int)((t + echo * .55f) * Rate), len = (int)(1.2f * Rate); float ph = 0, a = echo == 0 ? .8f : .3f;
                for (int j = 0; j < len && p + j < g.Length; j++)
                {
                    float u = j / (float)Rate; ph += Tau * (36 + 70 * MathF.Exp(-u * 16)) / Rate;
                    g[p + j] += MathF.Sin(ph) * MathF.Exp(-u * 4.2f) * a + rumble.Do(r.F()) * 2.2f * MathF.Exp(-u * 3f) * a;
                }
            }
            int pops = 25 + (int)(r.U() * 50); var shape = new Lp(6500);
            for (int c = 0; c < pops; c++) { float dt = .35f + 1.5f * MathF.Pow(r.U(), 1.6f); Burst(g, (int)((t + dt) * Rate), r.Between(.02f, .1f), .002f, r, shape); }
        }
    }

    private static void Whispers(float[] g, Rng r, double sec)
    {
        var f1 = new Bp(600, 2.2f); var f2 = new Bp(2000, 2.5f); var air = new Bp(5200, .7f);
        for (int i = 0; i < g.Length; i++)
        {
            if ((i & 63) == 0)
            {
                f1.Set(560 + 330 * Lfo(i, sec, 3), 2.2f); f2.Set(1900 + 650 * Lfo(i, sec, 5, 1.1f), 2.5f);
            }
            float w = r.F(), breath = 0.3f + 0.7f * MathF.Pow(0.5f + 0.5f * Lfo(i, sec, 6, 0.4f), 1.4f);
            g[i] = (f1.Do(w) * .5f + f2.Do(w) * .9f + air.Do(w) * .35f) * breath;
        }
    }
}

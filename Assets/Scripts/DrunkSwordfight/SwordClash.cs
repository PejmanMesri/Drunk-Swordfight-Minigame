using UnityEngine;

/// <summary>
/// Sword-vs-sword blocking. Host only: for every pair of live blades, if the
/// two blade segments come within ClashRadius of each other, both swords are
/// "parried" — their sweeps deal no damage for a short window and both swings
/// bounce apart with a clang. Blades resting against each other keep
/// refreshing the parry (a sword lock), so you can physically hold someone's
/// blade off you.
///
/// Each wielder's FixedUpdate calls RunHostPairs(this) for pairs with all
/// LATER-registered wielders, so every unordered pair is processed exactly
/// once per physics step regardless of script order.
/// </summary>
public static class SwordClash
{
    /// <summary>Blade segments closer than this clash.</summary>
    public const float ClashRadius = 0.18f;

    /// <summary>Relative contact-point closing speed for the loud, bouncy clash.</summary>
    public const float StrongClashClosing = 2.5f;

    internal static void RunHostPairs(SwordWielder wielder)
    {
        if (!wielder.IsClashable) return;

        var all = SwordWielder.All;
        for (int i = wielder.RegistryIndex + 1; i < all.Count; i++)
        {
            var other = all[i];
            if (other == null || !other.IsClashable) continue;
            TryClash(wielder, other);
        }
    }

    private static void TryClash(SwordWielder a, SwordWielder b)
    {
        ClosestSegSeg(a.Pivot, a.TipPos, b.Pivot, b.TipPos, out var pa, out var pb);
        float dist = Vector3.Distance(pa, pb);
        if (dist > ClashRadius) return;

        // normal pointing from a's blade toward b's blade
        Vector3 n = dist > 1e-4f ? (pb - pa) / dist : Vector3.Cross(a.Forward, b.Forward).normalized;
        if (n.sqrMagnitude < 0.5f) n = Vector3.up;

        float closing = Vector3.Dot(b.VelAt(pb) - a.VelAt(pa), n);
        bool strong = closing > StrongClashClosing;

        var mid = (pa + pb) * 0.5f;
        a.HostClash(n, closing, strong, mid);
        b.HostClash(-n, closing, strong, mid);
    }

    /// <summary>Closest points between two segments (Ericson, Real-Time Collision Detection).</summary>
    internal static void ClosestSegSeg(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2,
                                       out Vector3 c1, out Vector3 c2)
    {
        Vector3 d1 = q1 - p1, d2 = q2 - p2, r = p1 - p2;
        float a = Vector3.Dot(d1, d1), e = Vector3.Dot(d2, d2), f = Vector3.Dot(d2, r);
        float s, t;
        const float eps = 1e-8f;

        if (a <= eps && e <= eps) { c1 = p1; c2 = p2; return; }
        if (a <= eps) { s = 0f; t = Mathf.Clamp01(f / e); }
        else
        {
            float c = Vector3.Dot(d1, r);
            if (e <= eps)
            {
                t = 0f;
                s = Mathf.Clamp01(-c / a);
            }
            else
            {
                float b = Vector3.Dot(d1, d2);
                float denom = a * e - b * b;
                s = denom > eps ? Mathf.Clamp01((b * f - c * e) / denom) : 0f;
                t = (b * s + f) / e;
                if (t < 0f) { t = 0f; s = Mathf.Clamp01(-c / a); }
                else if (t > 1f) { t = 1f; s = Mathf.Clamp01((b - c) / a); }
            }
        }

        c1 = p1 + d1 * s;
        c2 = p2 + d2 * t;
    }
}

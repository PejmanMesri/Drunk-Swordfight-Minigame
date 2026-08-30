using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// The sword. This is the whole "mouse wields the sword" gimmick:
///
/// The blade direction is a damped spring chasing the fighter's aim. Whip
/// your view around and the sword whips after it with weight and lag — tip
/// speed is what deals damage, so drunken flailing IS the combat system.
/// Drunk levels make the spring floppier and inject wobble torque, so the
/// same mouse motion produces a lazier, less controlled swing.
///
/// The spring runs on EVERY peer for visuals (each one simulates locally
/// from the synced body yaw + aim pitch). Only the host judges hits, using
/// capsule sweeps between the last blade-tip position and the current one.
/// </summary>
public class SwordWielder : MonoBehaviour
{
    [Header("Spring (tune the feel here)")]
    [SerializeField] private float stiffness = 55f;
    [SerializeField] private float damping = 6.5f;
    [SerializeField] private float maxAngVel = 22f;      // rad/s
    [SerializeField] private float stabImpulse = 26f;    // rad/s forward kick

    [Header("Damage")]
    [SerializeField] private float bladeLength = 0.85f;
    [SerializeField] private float minHitSpeed = 3.5f;   // m/s at the tip
    [SerializeField] private float maxHitSpeed = 11f;
    [SerializeField] private float damageAtMinSpeed = 16f;
    [SerializeField] private float damageAtMaxSpeed = 32f;
    [SerializeField] private float hitCooldownPerVictim = 0.45f;

    private DrunkFightPlayer _owner;
    private Vector3 _dir;         // current blade direction (world, normalized)
    private Vector3 _angVel;      // angular velocity (world axis * rad/s)
    private Vector3 _prevTip;

    private readonly Dictionary<DrunkFightPlayer, float> _lastHitAt = new();
    private float _lastWhooshAt;
    private float _lastClangAt;
    private bool _wieldable = true;
    private float _noiseSeed;

    // ---------------------------------------------------------------- setup

    public void Initialize(DrunkFightPlayer owner)
    {
        _owner = owner;
        _noiseSeed = Random.value * 100f;
        _dir = transform.parent.forward;                 // start pointing where the body faces
        _prevTip = TipPosition;
        BuildMesh();
    }

    public void SetWieldable(bool wieldable) => _wieldable = wieldable;

    private Vector3 TipPosition => transform.position + _dir * bladeLength;

    // ---------------------------------------------------------------- simulation

    private void FixedUpdate()
    {
        if (_owner == null) return;

        float dt = Time.fixedDeltaTime;
        Simulate(dt);

        if (_owner.IsServer && _wieldable && _owner.Alive.Value && !_owner.Frozen.Value)
            HostSweepForDamage(dt);
    }

    private void Simulate(float dt)
    {
        int drunk = _owner.DrunkLevel.Value;
        bool dead = !_owner.Alive.Value;

        // where the fighter wants the sword: along their aim (pitch is +down,
        // same convention as the camera)
        float yaw = transform.parent.eulerAngles.y;
        Vector3 target = dead
            ? Vector3.down
            : Quaternion.Euler(_owner.AimPitch.Value, yaw, 0f) * Vector3.forward;

        // drunk: floppier spring, plus wobble torque
        float k = stiffness * (1f - 0.20f * drunk) * (dead ? 0.4f : 1f);
        float c = Mathf.Max(1f, damping - 0.55f * drunk);

        _angVel += (target - _dir) * k * dt;
        _angVel *= Mathf.Max(0f, 1f - c * dt);

        // gravity droop + drunk noise, applied around the current swing plane
        Vector3 axisRight = Vector3.Cross(Vector3.up, _dir);
        if (axisRight.sqrMagnitude < 1e-4f) axisRight = Vector3.right;
        axisRight.Normalize();
        Vector3 axisUp = Vector3.Cross(_dir, axisRight).normalized;
        _angVel -= axisUp * 2.5f * dt; // slight weight
        if (drunk > 0 && !dead)
        {
            float t = Time.time + _noiseSeed;
            float nx = Mathf.PerlinNoise(t * 1.1f, 0.3f) - 0.5f;
            float ny = Mathf.PerlinNoise(t * 0.9f, 7.7f) - 0.5f;
            _angVel += (axisRight * nx + axisUp * ny) * (9f * drunk) * dt;
        }

        if (_angVel.sqrMagnitude > maxAngVel * maxAngVel)
            _angVel = _angVel.normalized * maxAngVel;

        _dir = (_dir + _angVel * dt).normalized;
        transform.rotation = Quaternion.LookRotation(_dir, Vector3.up);
    }

    // ---------------------------------------------------------------- stabbing

    /// <summary>Host only: lunge the blade straight forward.</summary>
    public void HostStab()
    {
        _angVel += transform.parent.forward * stabImpulse;
        _owner.WielderFxClientRpc();
    }

    /// <summary>Runs on every peer so everyone sees/hears the stab locally.</summary>
    public void ClientStab()
    {
        _angVel += transform.parent.forward * stabImpulse;
    }

    // ---------------------------------------------------------------- host: damage

    private void HostSweepForDamage(float dt)
    {
        Vector3 prevTip = _prevTip;
        Vector3 tip = TipPosition;
        Vector3 tipVel = (tip - prevTip) / dt;
        float speed = tipVel.magnitude;
        _prevTip = tip;

        if (speed < minHitSpeed) return;

        // sweep the tip path AND the blade body for victims
        var hits = new HashSet<DrunkFightPlayer>();
        CollectVictims(Physics.OverlapCapsule(prevTip, tip, 0.12f), hits);
        Vector3 mid = transform.position + _dir * (bladeLength * 0.5f);
        CollectVictims(Physics.OverlapCapsule(transform.position, mid, 0.10f), hits);
        CollectVictims(Physics.OverlapCapsule(mid, tip, 0.10f), hits);

        bool hitAnything = false;
        foreach (var victim in hits)
        {
            if (_lastHitAt.TryGetValue(victim, out float t) && Time.time - t < hitCooldownPerVictim) continue;
            _lastHitAt[victim] = Time.time;
            hitAnything = true;

            float damage = Mathf.Lerp(damageAtMinSpeed, damageAtMaxSpeed,
                Mathf.InverseLerp(minHitSpeed, maxHitSpeed, speed));
            damage *= Random.Range(0.85f, 1.15f);
            victim.HostTakeDamage(damage, tipVel.normalized, _owner.OwnerClientId);
        }

        // clanging off furniture is free comedy
        if (!hitAnything && Time.time - _lastClangAt > 0.3f)
        {
            var solid = Physics.OverlapCapsule(prevTip, tip, 0.12f);
            if (solid.Length > 0)
            {
                _lastClangAt = Time.time;
                Fx.Sparks(tip, tipVel.normalized, 6f);
                _owner.PlayClingClientRpc();
            }
        }

        if (speed > 5f && Time.time - _lastWhooshAt > 0.6f)
        {
            _lastWhooshAt = Time.time;
            _owner.PlayWhooshClientRpc();
        }
    }

    private void CollectVictims(Collider[] colliders, HashSet<DrunkFightPlayer> into)
    {
        foreach (var col in colliders)
        {
            var dfp = col.GetComponentInParent<DrunkFightPlayer>();
            if (dfp == null || dfp == _owner || !dfp.Alive.Value || !dfp.GearActive) continue;
            into.Add(dfp);
        }
    }

    // ---------------------------------------------------------------- mesh (procedural)

    private void BuildMesh()
    {
        var steel = LoadMat("DrunkFight/Mats/Steel");
        var brass = LoadMat("DrunkFight/Mats/Brass");
        var leather = LoadMat("DrunkFight/Mats/Leather");

        // grip along +Z
        var grip = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Name(grip.transform, "Grip");
        grip.transform.SetParent(transform, false);
        grip.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        grip.transform.localScale = new Vector3(0.032f, 0.09f, 0.032f);
        grip.transform.localPosition = new Vector3(0f, 0f, -0.04f);
        SetMat(grip, leather);
        NoCollide(grip);

        var pommel = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Name(pommel.transform, "Pommel");
        pommel.transform.SetParent(transform, false);
        pommel.transform.localScale = Vector3.one * 0.05f;
        pommel.transform.localPosition = new Vector3(0f, 0f, -0.14f);
        SetMat(pommel, brass);
        NoCollide(pommel);

        var guard = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Name(guard.transform, "Guard");
        guard.transform.SetParent(transform, false);
        guard.transform.localScale = new Vector3(0.20f, 0.028f, 0.045f);
        guard.transform.localPosition = new Vector3(0f, 0f, 0.05f);
        SetMat(guard, brass);
        NoCollide(guard);

        var blade = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Name(blade.transform, "Blade");
        blade.transform.SetParent(transform, false);
        blade.transform.localScale = new Vector3(0.055f, 0.014f, 0.62f);
        blade.transform.localPosition = new Vector3(0f, 0f, 0.37f);
        SetMat(blade, steel);
        NoCollide(blade);

        var tip = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Name(tip.transform, "Tip");
        tip.transform.SetParent(transform, false);
        tip.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);
        tip.transform.localScale = new Vector3(0.055f, 0.055f, 0.10f);
        tip.transform.localPosition = new Vector3(0f, 0f, 0.73f);
        SetMat(tip, steel);
        NoCollide(tip);
    }

    private static void Name(Transform t, string n) => t.name = n;
    private static void NoCollide(GameObject go) => Destroy(go.GetComponent<Collider>());
    private static void SetMat(GameObject go, Material m)
    {
        var r = go.GetComponent<MeshRenderer>();
        if (m != null) r.sharedMaterial = m;
    }

    internal static Material LoadMat(string path)
    {
        var m = Resources.Load<Material>(path);
        if (m != null) return m;

        // fallback so the game still works before the editor step has run
        var lit = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        lit.color = new Color(0.7f, 0.7f, 0.72f);
        return lit;
    }
}

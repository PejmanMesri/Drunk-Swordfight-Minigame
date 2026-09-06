using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Owner-only FIRST PERSON camera. Sits in the head, aims with the mouse,
/// and layers on perlin wobble, roll and hit-shake that scale with the
/// drunk level, plus the local post-processing (vignette, chromatic
/// aberration, lens distortion). The more you sip, the less you trust
/// your eyes.
/// </summary>
public class DrunkFightCamera : MonoBehaviour
{
    public const float YawPerPixel = 0.11f;
    public const float PitchPerPixel = 0.09f;

    private const float HeadHeight = 0.62f;     // root-local, eye-ish height
    private const float ForwardOffset = 0.09f;  // keep the head mesh out of the lens

    private DrunkFightPlayer _owner;
    private Transform _target;
    private Camera _cam;
    private float _shake;
    private float _fovKick;
    private float _spectatorBlend;

    public float Yaw { get; private set; }
    public float Pitch { get; private set; } = 0f;

    private GameObject _disabledMainCamera;

    public static DrunkFightCamera Create(DrunkFightPlayer owner)
    {
        var go = new GameObject("DrunkFightCamera");
        var cam = go.AddComponent<DrunkFightCamera>();
        cam._owner = owner;
        cam._target = owner.transform;
        cam.Yaw = owner.transform.eulerAngles.y;

        cam._cam = go.AddComponent<Camera>();
        cam._cam.fieldOfView = 70f;
        cam._cam.nearClipPlane = 0.06f;
        cam._cam.farClipPlane = 80f;
        go.AddComponent<AudioListener>();       // the Hub camera (and its listener) gets parked

        // the Hub's camera would render under ours; disable it locally
        var main = Camera.main;
        if (main != null && main.transform != go.transform)
        {
            cam._disabledMainCamera = main.gameObject;
            main.gameObject.SetActive(false);
        }

        return cam;
    }

    private void OnDestroy()
    {
        if (_disabledMainCamera != null) _disabledMainCamera.SetActive(true);
    }

    /// <summary>Called by DrunkFightPlayer with the raw mouse delta.
    /// Pitch is +down (Unity Euler X), so mouse-up decreases it.</summary>
    public void AddLook(Vector2 delta)
    {
        Yaw = MathUtil.NormalizeAngleDeg(Yaw + delta.x * YawPerPixel);
        Pitch = Mathf.Clamp(Pitch - delta.y * PitchPerPixel, -60f, 70f);
        _owner.AimPitch.Value = Pitch;          // syncs to everyone (owner-write)
    }

    public void Shake(float amount01) => _shake = Mathf.Max(_shake, amount01);

    /// <summary>Brief FOV punch, used on stabs.</summary>
    public void FovKick() => _fovKick = 9f;

    private void LateUpdate()
    {
        if (_target == null) return;

        float t = Time.time;
        float drunk = _owner != null ? _owner.DrunkLevel.Value : 0f;
        float wob = 0.15f + drunk * 0.72f;      // even sober, the room tilts a little

        float yawOff = (Mathf.PerlinNoise(t * 0.55f, 3.1f) - 0.5f) * 5.5f * wob;
        float pitchOff = (Mathf.PerlinNoise(t * 0.72f, 8.2f) - 0.5f) * 4.5f * wob;
        float roll = (Mathf.PerlinNoise(t * 0.40f, 12.3f) - 0.5f) * 13f * wob;

        // sit in the head (the model's head is invisible from inside thanks
        // to backface culling), look where the mouse points
        Vector3 fpPos = _target.position
            + Vector3.up * HeadHeight
            + _target.forward * ForwardOffset;
        Quaternion fpRot = Quaternion.Euler(Pitch + pitchOff, Yaw + yawOff, 0f)
            * Quaternion.Euler(0f, 0f, roll);

        // once you're down, drift out of your corpse into a slow orbit
        bool dead = _owner != null && !_owner.Alive.Value;
        _spectatorBlend = Mathf.MoveTowards(_spectatorBlend, dead ? 1f : 0f, Time.deltaTime * 0.5f);

        if (_spectatorBlend > 0.001f)
        {
            float angle = t * 0.25f;
            var center = _target.position + Vector3.up * 0.6f;
            Vector3 orbit = center + new Vector3(Mathf.Sin(angle) * 5.5f, 3.2f, Mathf.Cos(angle) * 5.5f);
            Quaternion orbitRot = Quaternion.LookRotation(center - orbit);

            transform.position = Vector3.Lerp(fpPos, orbit, _spectatorBlend);
            transform.rotation = Quaternion.Slerp(fpRot, orbitRot, _spectatorBlend);
        }
        else
        {
            transform.position = fpPos;
            transform.rotation = fpRot;
        }

        if (_shake > 0.001f)
        {
            transform.position += new Vector3(
                (Mathf.PerlinNoise(t * 40f, 1f) - 0.5f),
                (Mathf.PerlinNoise(t * 40f, 9f) - 0.5f),
                0f) * (_shake * 0.10f);
            _shake = Mathf.MoveTowards(_shake, 0f, Time.deltaTime * 3.5f);
        }

        _fovKick = Mathf.MoveTowards(_fovKick, 0f, Time.deltaTime * 45f);
        if (_cam != null)
            _cam.fieldOfView = 70f + Mathf.Sin(t * 0.6f) * 2f * wob + _fovKick;

        UpdatePostFx(drunk);
    }

    private void UpdatePostFx(float drunk)
    {
        var profile = DrunkSwordfightMinigame.Instance?.FxProfile;
        if (profile == null) return;

        if (profile.TryGet<Vignette>(out var vig))
            vig.intensity.value = Mathf.Lerp(vig.intensity.value, 0.20f + 0.16f * drunk, 0.05f);
        if (profile.TryGet<ChromaticAberration>(out var ca))
            ca.intensity.value = Mathf.Lerp(ca.intensity.value, 0.08f + 0.30f * drunk, 0.05f);
        if (profile.TryGet<LensDistortion>(out var lens))
            lens.intensity.value = Mathf.Lerp(lens.intensity.value, -14f * drunk, 0.05f);
        if (profile.TryGet<ColorAdjustments>(out var color))
            color.saturation.value = Mathf.Lerp(color.saturation.value, -7f * drunk, 0.05f);
    }
}

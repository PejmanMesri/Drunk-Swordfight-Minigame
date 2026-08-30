using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Owner-only third person camera. Follows the fighter's aim (the fighter
/// script feeds it mouse deltas), adds perlin wobble, roll, breathing and
/// hit shake that all scale with the drunk level, and drives the local
/// post-processing (vignette, chromatic aberration, lens distortion) the
/// same way. The more you sip, the less you trust your eyes.
/// </summary>
public class DrunkFightCamera : MonoBehaviour
{
    public const float YawPerPixel = 0.11f;
    public const float PitchPerPixel = 0.09f;

    private DrunkFightPlayer _owner;
    private Transform _target;
    private Camera _cam;
    private Vector3 _posVel;
    private float _shake;

    public float Yaw { get; private set; }
    public float Pitch { get; private set; } = 12f;

    private GameObject _disabledMainCamera;

    public static DrunkFightCamera Create(DrunkFightPlayer owner)
    {
        var go = new GameObject("DrunkFightCamera");
        var cam = go.AddComponent<DrunkFightCamera>();
        cam._owner = owner;
        cam._target = owner.transform;
        cam.Yaw = owner.transform.eulerAngles.y;

        cam._cam = go.AddComponent<Camera>();
        cam._cam.fieldOfView = 60f;
        cam._cam.nearClipPlane = 0.1f;
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
        Pitch = Mathf.Clamp(Pitch - delta.y * PitchPerPixel, -35f, 55f);
        _owner.AimPitch.Value = Pitch;          // syncs to everyone (owner-write)
    }

    public void Shake(float amount01) => _shake = Mathf.Max(_shake, amount01);

    private void LateUpdate()
    {
        if (_target == null) return;

        float t = Time.time;
        float drunk = _owner != null ? _owner.DrunkLevel.Value : 0f;
        float wob = drunk * 0.55f;

        float yawOff = (Mathf.PerlinNoise(t * 0.55f, 3.1f) - 0.5f) * 4.5f * wob;
        float pitchOff = (Mathf.PerlinNoise(t * 0.72f, 8.2f) - 0.5f) * 3.6f * wob;
        float roll = (Mathf.PerlinNoise(t * 0.40f, 12.3f) - 0.5f) * 14f * wob;

        var pivot = _target.position + Vector3.up * 1.5f;
        float dist = 3.4f + Mathf.Sin(t * 0.9f) * 0.05f + wob * 0.18f;
        var rot = Quaternion.Euler(Pitch + pitchOff, Yaw + yawOff, 0f);
        var wanted = pivot - rot * Vector3.forward * dist;

        transform.position = Vector3.SmoothDamp(transform.position, wanted, ref _posVel, 0.08f);

        var lookRot = Quaternion.LookRotation(pivot + rot * Vector3.forward * 0.8f - transform.position);
        transform.rotation = lookRot * Quaternion.Euler(0f, 0f, roll);

        // hit shake
        if (_shake > 0.001f)
        {
            transform.position += new Vector3(
                (Mathf.PerlinNoise(t * 40f, 1f) - 0.5f),
                (Mathf.PerlinNoise(t * 40f, 9f) - 0.5f),
                0f) * (_shake * 0.35f);
            _shake = Mathf.MoveTowards(_shake, 0f, Time.deltaTime * 2.5f);
        }

        if (_cam != null) _cam.fieldOfView = 60f + Mathf.Sin(t * 0.6f) * 1.5f * wob;

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

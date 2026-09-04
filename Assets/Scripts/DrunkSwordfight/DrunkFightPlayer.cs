using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// The drunk swordfighter. Sits on the player avatar prefab next to
/// MinigamePlayerController and only wakes up when the MG_DrunkSwordfight
/// scene is loaded — in every other minigame it stays dormant and invisible.
///
/// Same rule as everywhere in this project: the client sends INTENT
/// (aim, stab, sip) via ServerRpc, the host decides everything. HP, sips
/// and drunk level live in NetworkVariables so every peer can draw bars.
///
/// Gear (sword, bottle, overhead bar, camera, HUD) is built procedurally at
/// runtime on every peer, so nothing here needs prefab authoring.
/// </summary>
public class DrunkFightPlayer : NetworkBehaviour
{
    public const string SceneName = "MG_DrunkSwordfight";

    [Header("Tuning")]
    [SerializeField] private float maxHp = 100f;
    [SerializeField] private float sipHeal = 35f;
    [SerializeField] private byte sipsPerBottle = 3;
    [SerializeField] private float sipLockSeconds = 0.9f;

    [Header("Sword (built at runtime). The pivot rides on the character model," +
            " whose origin sits at the FEET after ground calibration.")]
    [SerializeField] private float shoulderHeightAboveFeet = 1.45f;
    [SerializeField] private float shoulderSide = 0.26f;

    public float MaxHp => maxHp;

    // ---- replicated state -------------------------------------------------

    public NetworkVariable<float> Hp =
        new(100f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<byte> SipsLeft =
        new(3, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<byte> DrunkLevel =
        new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<bool> Alive =
        new(true, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<bool> Frozen =
        new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<FixedString64Bytes> FighterName =
        new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<float> AimPitch =
        new(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    // ---- locally built gear ------------------------------------------------

    private SwordWielder _sword;
    private Transform _bottle;
    private Vector3 _bottleRestPos;
    private Quaternion _bottleRestRot;
    private OverheadHud _overhead;
    private DrunkFightCamera _camera;
    private FighterHud _hud;
    private AudioSource _audio;

    private Rigidbody _rb;
    private MinigamePlayerController _controller;
    private AvatarAnimatorDriver _driver;
    private Transform _model;
    private float _swaySeed;

    // owner-side input state
    private float _localYaw;
    private float _lastSentYaw = float.MinValue;
    private float _sipAnimTimer;      // runs on every peer for the glug animation

    // host-side state
    private float _hostYaw;
    private float _hostSipLock;
    private float _drunkYawNoiseSeed;

    public bool GearActive { get; private set; }
    public SwordWielder Sword => _sword;

    // ---------------------------------------------------------------- setup

    public override void OnNetworkSpawn()
    {
        _rb = GetComponent<Rigidbody>();
        _controller = GetComponent<MinigamePlayerController>();

        if (IsServer)
        {
            Hp.Value = maxHp;
            SipsLeft.Value = sipsPerBottle;
            DrunkLevel.Value = 0;
            Alive.Value = true;
            Frozen.Value = true;                       // frozen until "FIGHT!"
            FighterName.Value = new FixedString64Bytes($"Player {OwnerClientId}");
            _hostYaw = transform.eulerAngles.y;
            _drunkYawNoiseSeed = Random.value * 100f;
        }

        if (SceneUtil.IsSceneLoaded(SceneName))
            ActivateGear();
    }

    public override void OnNetworkDespawn()
    {
        DeactivateGear();
    }

    /// <summary>Idempotent. Normally called from OnNetworkSpawn; kept public so
    /// tests and edge cases can force it.</summary>
    public void ActivateGear()
    {
        if (GearActive) return;
        GearActive = true;

        _driver = GetComponent<AvatarAnimatorDriver>();
        _model = transform.Find("Model");
        _swaySeed = Random.value * 100f;

        BuildSword();
        BuildBottle();
        _driver?.SetSwordGrip(this, _sword);
        _overhead = OverheadHud.Create(this);
        _audio = gameObject.AddComponent<AudioSource>();
        _audio.spatialBlend = 1f;
        _audio.rolloffMode = AudioRolloffMode.Linear;
        _audio.maxDistance = 30f;
        _audio.playOnAwake = false;

        if (IsOwner)
        {
            _camera = DrunkFightCamera.Create(this);
            _hud = FighterHud.Create(this);
            _localYaw = transform.eulerAngles.y;
            _lastSentYaw = _localYaw;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    public void DeactivateGear()
    {
        if (!GearActive) return;
        GearActive = false;

        _driver?.SetSwordGrip(null, null);
        if (_model != null) _model.localRotation = Quaternion.identity;

        if (_camera != null) Destroy(_camera.gameObject);
        if (_hud != null) Destroy(_hud.gameObject);
        if (_overhead != null) Destroy(_overhead.gameObject);

        if (IsOwner)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    private void BuildSword()
    {
        // parent under the character model so the blade rides along with the
        // ground-calibrated body (falls back to the root if no model exists)
        var model = transform.Find("Model");
        var parent = model != null ? model.transform : transform;

        var pivotGo = new GameObject("SwordPivot");
        pivotGo.transform.SetParent(parent, false);

        // the model carries a fit scale (~0.93), so express the shoulder in
        // meters-above-feet and undo the parent scale
        var s = parent.lossyScale;
        pivotGo.transform.localPosition = new Vector3(
            shoulderSide / s.x, shoulderHeightAboveFeet / s.y, 0.04f / s.z);

        _sword = pivotGo.AddComponent<SwordWielder>();
        _sword.Initialize(this);
    }

    private void BuildBottle()
    {
        // front-left hip: visible in first person when you glance down
        _bottle = WhiskeyBottle.Build(transform, new Vector3(-0.18f, 0.05f, 0.22f));
        _bottleRestPos = _bottle.localPosition;
        _bottleRestRot = _bottle.localRotation;
    }

    // ---------------------------------------------------------------- frame

    private void Update()
    {
        if (!GearActive) return;

        float dt = Time.deltaTime;

        // bottle sip animation runs on every peer so everyone sees the glug.
        // In first person the bottle is parked on the FRONT hip (visible when
        // you look down) and swings up right in front of your face to drink.
        if (_sipAnimTimer > 0f)
        {
            _sipAnimTimer -= dt;
            float t = 1f - Mathf.Clamp01(_sipAnimTimer / sipLockSeconds);
            float raise = Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI);   // 0 -> 1 -> 0
            _bottle.localPosition = Vector3.Lerp(_bottleRestPos,
                _bottleRestPos + new Vector3(0.12f, 0.50f, 0.42f), raise);
            _bottle.localRotation = _bottleRestRot * Quaternion.Euler(-40f * raise, 0f, 0f);
        }
        _bottle.gameObject.SetActive(SipsLeft.Value > 0 || _sipAnimTimer > 0f);

        if (IsOwner) OwnerInputTick();

        if (IsServer) ServerTick(dt);

        // drunk fighters can't stand still: subtle body sway on the model
        if (_model != null && Alive.Value)
        {
            float wob = DrunkLevel.Value * 1.6f;
            float t = Time.time + _swaySeed;
            float leanX = (Mathf.PerlinNoise(t * 0.8f, 1.7f) - 0.5f) * wob;
            float leanZ = (Mathf.PerlinNoise(t * 0.65f, 9.3f) - 0.5f) * wob * 1.4f;
            _model.localRotation = Quaternion.Euler(leanX, 0f, leanZ);
        }
    }

    private void OwnerInputTick()
    {
#if ENABLE_INPUT_SYSTEM
        var mouse = Mouse.current;
        var kb = Keyboard.current;

        if (kb != null && kb.qKey.wasPressedThisFrame)
            TrySipServerRpc();

        if (mouse == null) return;

        // ESC lets go of the cursor, the next click grabs it again
        if (kb != null && kb.escapeKey.wasPressedThisFrame)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        if (Cursor.lockState != CursorLockMode.Locked)
        {
            _controller?.SetMoveInputOverride(null);
            if (mouse.leftButton.wasPressedThisFrame)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            return; // no game input while the cursor is free
        }

        if (!Alive.Value || Frozen.Value)
        {
            _controller?.SetMoveInputOverride(null);
            return;
        }

        if (_camera == null) return;

        var delta = mouse.delta.ReadValue();
        _camera.AddLook(delta);
        _localYaw = _camera.Yaw;

        if (mouse.leftButton.wasPressedThisFrame)
            StabServerRpc();

        // tell the host where we're facing (it owns the rigidbody)
        if (Mathf.Abs(Mathf.DeltaAngle(_lastSentYaw, _localYaw)) > 0.4f)
        {
            _lastSentYaw = _localYaw;
            SendYawServerRpc(_localYaw);
        }

        // movement is camera-relative: rotate WASD intent by our view yaw
        if (_controller != null)
        {
            Vector2 wasd = MinigamePlayerController.ReadWasd();
            _controller.SetMoveInputOverride(MathUtil.Rotate(wasd, -_localYaw));
        }
#endif
    }

    private void ServerTick(float dt)
    {
        // keep the controller parked while frozen (countdown) or dead
        if (_controller != null)
            _controller.enabled = Alive.Value && !Frozen.Value;

        if (!Alive.Value) return;

        if (_hostSipLock > 0f) _hostSipLock -= dt;

        // drunk fighters slowly veer off course even when walking straight
        float wander = Mathf.PerlinNoise(_drunkYawNoiseSeed + Time.time * 0.13f, 0f) - 0.5f;
        _rb.rotation = Quaternion.Euler(0f, _hostYaw + wander * DrunkLevel.Value * 7f, 0f);
    }

    // ---------------------------------------------------------------- input rpcs

    [ServerRpc]
    private void SendYawServerRpc(float yawDeg)
    {
        _hostYaw = MathUtil.NormalizeAngleDeg(yawDeg);
    }

    [ServerRpc]
    private void StabServerRpc()
    {
        if (!Alive.Value || Frozen.Value) return;
        _sword?.HostStab();
    }

    [ServerRpc]
    private void TrySipServerRpc()
    {
        if (!Alive.Value || Frozen.Value) return;
        if (SipsLeft.Value <= 0 || _hostSipLock > 0f) return;

        _hostSipLock = sipLockSeconds;
        SipsLeft.Value--;
        DrunkLevel.Value = (byte)Mathf.Min(DrunkLevel.Value + 1, 3);
        Hp.Value = Mathf.Min(maxHp, Hp.Value + sipHeal);

        SipClientRpc();
    }

    [ClientRpc]
    private void SipClientRpc()
    {
        _sipAnimTimer = sipLockSeconds;
        if (IsOwner) _hud?.FlashSip();
        if (_audio != null) _audio.PlayOneShot(ProceduralAudio.Glug(), 0.7f);
    }

    /// <summary>Host calls this from SwordWielder.HostStab so every peer
    /// kicks the local blade spring and hears the lunge.</summary>
    [ClientRpc]
    public void WielderFxClientRpc()
    {
        _sword?.ClientStab();
        if (_audio != null)
        {
            _audio.pitch = Random.Range(0.85f, 1.15f);
            _audio.PlayOneShot(ProceduralAudio.Whoosh(), 0.6f);
        }
        if (IsOwner) _camera?.FovKick();
    }

    [ClientRpc]
    public void PlayWhooshClientRpc()
    {
        if (_audio != null)
        {
            _audio.pitch = Random.Range(0.8f, 1.2f);
            _audio.PlayOneShot(ProceduralAudio.Whoosh(), 0.5f);
        }
    }

    [ClientRpc]
    public void PlayClingClientRpc()
    {
        if (_audio != null) _audio.PlayOneShot(ProceduralAudio.Clang(), 0.4f);
    }

    // ---------------------------------------------------------------- clashing

    /// <summary>Host only: stagger when blades bounce off each other. Scaled
    /// by how hard the two swords were closing — real clashes SHOVE.</summary>
    internal void HostClashPush(Vector3 awayDir, float closing)
    {
        if (!IsServer) return;
        float power = Mathf.Min(1.5f + closing * 0.9f, 7f);
        var v = awayDir.normalized * power;
        _rb.linearVelocity += new Vector3(v.x, 0.9f + power * 0.08f, v.z);
    }

    /// <summary>Host only: tell every peer a blade clash happened.</summary>
    internal void HostReportClash(Vector3 point, Vector3 awayNormal, float strength)
    {
        BladeClashClientRpc(point, awayNormal, strength);
    }

    [ClientRpc]
    private void BladeClashClientRpc(Vector3 point, Vector3 awayNormal, float strength)
    {
        // the host already applied the impulse in SwordWielder.HostClash
        if (!IsServer) _sword?.ClientClash(awayNormal, strength);

        Fx.Sparks(point, awayNormal, 8f * strength);
        if (_audio != null) _audio.PlayOneShot(ProceduralAudio.Clang(), 0.25f + 0.35f * strength);
        if (IsOwner) _camera?.Shake(0.25f * strength);
    }

    // ---------------------------------------------------------------- damage (host only)

    /// <summary>Host only. Called by SwordWielder sweeps and the minigame.</summary>
    public void HostTakeDamage(float amount, Vector3 impulseDir, ulong attackerId)
    {
        if (!IsServer || !Alive.Value) return;

        Hp.Value = Mathf.Max(0f, Hp.Value - amount);

        // MEATY knockback: a clean hit should launch people across the tavern
        var push = impulseDir.normalized * Mathf.Min(4f + amount * 0.12f, 9f);
        _rb.linearVelocity += new Vector3(push.x, 1.1f, push.z);

        HitClientRpc(transform.position + Vector3.up * 1.2f, impulseDir, amount, attackerId);

        if (Hp.Value <= 0f)
            HostDie();
    }

    private void HostDie()
    {
        Alive.Value = false;
        if (_controller != null) _controller.enabled = false;

        // face-plant: let physics tumble the body
        _rb.constraints = RigidbodyConstraints.None;
        _rb.angularVelocity = new Vector3(
            Random.Range(-3f, 3f), Random.Range(-2f, 2f), Random.Range(-3f, 3f));
        _rb.linearVelocity += Vector3.up * 1.5f;
        _rb.mass = 80f; // heavy corpse, no bouncing around

        if (_sword != null) _sword.SetWieldable(false);
        if (_overhead != null) _overhead.SetDead();
    }

    [ClientRpc]
    private void HitClientRpc(Vector3 point, Vector3 dir, float amount, ulong attackerId)
    {
        Fx.Sparks(point, dir, amount);
        if (_audio != null)
        {
            _audio.PlayOneShot(ProceduralAudio.Thud(), 0.8f);
            _audio.PlayOneShot(ProceduralAudio.Clang(), 0.5f);
        }
        if (IsOwner) _hud?.FlashDamage(amount);
        if (_camera != null) _camera.Shake(Mathf.Clamp01(amount / 30f));
    }

    // ---------------------------------------------------------------- host helpers for the minigame

    /// <summary>Host only. Full reset for a new round.</summary>
    public void HostResetForRound(float facingYawDeg)
    {
        if (!IsServer) return;
        Hp.Value = maxHp;
        SipsLeft.Value = sipsPerBottle;
        DrunkLevel.Value = 0;
        Alive.Value = true;
        Frozen.Value = true;
        _hostYaw = facingYawDeg;
        _rb.constraints = RigidbodyConstraints.FreezeRotation;
        _rb.mass = 1f;
        _rb.rotation = Quaternion.Euler(0f, facingYawDeg, 0f);
        if (_controller != null) _controller.enabled = false;
        if (_sword != null) _sword.SetWieldable(true);
    }

    public void HostSetFrozen(bool frozen)
    {
        if (!IsServer) return;
        Frozen.Value = frozen;
        if (_controller != null && Alive.Value) _controller.enabled = !frozen;
    }
}

using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Drunk swordfight: everyone gets a sword, a bottle of whiskey and 100 HP.
/// Last one standing wins. Sipping (Q) heals but stacks drunk levels, which
/// make your aim, movement and sword control steadily worse.
///
/// Host-authoritative like everything else: this class resets fighters,
/// runs the countdown, watches HP and awards placements to the harness.
/// </summary>
public class DrunkSwordfightMinigame : MinigameBase
{
    [Header("Drunk Swordfight")]
    [SerializeField] private float countdownStep = 0.8f;
    [SerializeField] private float winnerLinger = 8f;
    [SerializeField] private float fallKillHeight = -8f;
    [SerializeField] private float roundIntervalSeconds = 30f;

    /// <summary>Replicated banner text ("3", "FIGHT!", "PLAYER 1 IS DOWN"...).</summary>
    public NetworkVariable<FixedString64Bytes> Announcement =
        new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    /// <summary>Local per-peer instance (for the camera to grab the FX volume).</summary>
    public static DrunkSwordfightMinigame Instance { get; private set; }

    public VolumeProfile FxProfile { get; private set; }

    private readonly List<ulong> _alive = new();
    private readonly List<ulong> _deadOrder = new();   // first eliminated first
    private readonly Dictionary<ulong, DrunkFightPlayer> _fighters = new();

    private bool _fightStarted;
    private bool _roundOver;
    private float _nextRoundAt;                         // next free drink pour
    private AudioSource _music;                        // 2D beeps/fanfare
    private readonly List<Light> _disabledForeignLights = new();
    private Color _oldAmbient;
    private bool _wasFog;
    private Color _oldFogColor;
    private float _oldFogDensity;

    // ---------------------------------------------------------------- lifetime

    public override void OnNetworkSpawn()
    {
        Instance = this;
        _music = gameObject.AddComponent<AudioSource>();
        _music.spatialBlend = 0f;
        _music.playOnAwake = false;

        // faint crowd murmur under everything: the tavern is alive
        _music.clip = ProceduralAudio.TavernMurmur();
        _music.loop = true;
        _music.volume = 0.10f;
        _music.Play();

        MakeTavernAtmosphere();
    }

    public override void OnNetworkDespawn()
    {
        RestoreAtmosphere();
        if (Instance == this) Instance = null;
    }

    /// <summary>
    /// The tavern is an additive scene, so the Hub's lighting settings win by
    /// default. Override them locally on every peer while we're loaded.
    /// </summary>
    private void MakeTavernAtmosphere()
    {
        _oldAmbient = RenderSettings.ambientLight;
        RenderSettings.ambientLight = new Color(0.27f, 0.21f, 0.16f);
        RenderSettings.ambientMode = AmbientMode.Flat;

        _wasFog = RenderSettings.fog;
        _oldFogColor = RenderSettings.fogColor;
        _oldFogDensity = RenderSettings.fogDensity;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = new Color(0.07f, 0.05f, 0.03f);
        RenderSettings.fogDensity = 0.008f;

        // the Hub's sun would flatten the mood; keep only our tavern lights
        foreach (var light in FindObjectsByType<Light>(FindObjectsInactive.Exclude))
        {
            if (light.gameObject.scene == gameObject.scene) continue;
            if (!light.enabled) continue;
            _disabledForeignLights.Add(light);
            light.enabled = false;
        }

        // post FX driven per-peer by the local player's drunk level
        var volumeGo = new GameObject("DrunkFxVolume");
        volumeGo.transform.SetParent(transform, false);
        var volume = volumeGo.AddComponent<Volume>();
        volume.isGlobal = true;
        FxProfile = ScriptableObject.CreateInstance<VolumeProfile>();
        volume.sharedProfile = FxProfile;
        FxProfile.Add<Vignette>(true);
        FxProfile.Add<ChromaticAberration>(true);
        FxProfile.Add<LensDistortion>(true);
        var color = FxProfile.Add<ColorAdjustments>(true);
        color.saturation.Override(0f);
        color.saturation.value = 0f;
    }

    private void RestoreAtmosphere()
    {
        RenderSettings.ambientLight = _oldAmbient;
        RenderSettings.fog = _wasFog;
        RenderSettings.fogColor = _oldFogColor;
        RenderSettings.fogDensity = _oldFogDensity;
        foreach (var light in _disabledForeignLights)
            if (light != null) light.enabled = true;
        _disabledForeignLights.Clear();
        if (FxProfile != null) Destroy(FxProfile);
    }

    // ---------------------------------------------------------------- round flow

    protected override void StartMinigame(ulong[] players)
    {
        _alive.Clear();
        _deadOrder.Clear();
        _fighters.Clear();
        _fightStarted = false;
        _roundOver = false;

        var points = SpawnPoints;
        for (int i = 0; i < players.Length; i++)
        {
            var id = players[i];
            if (!NetworkManager.ConnectedClients.TryGetValue(id, out var client) || client.PlayerObject == null)
            {
                _deadOrder.Add(id);      // vanished before we started
                continue;
            }

            var fighter = client.PlayerObject.GetComponent<DrunkFightPlayer>();
            if (fighter == null)
            {
                Debug.LogError($"[DrunkSwordfight] avatar for client {id} has no DrunkFightPlayer. " +
                               "Run the editor setup (Assets/Editor/DrunkSwordfightSetup).");
                _deadOrder.Add(id);
                continue;
            }

            var spawn = points != null && points.Length > 0 ? points[i % points.Length] : null;
            var pos = spawn != null ? spawn.position : Vector3.zero;
            fighter.HostResetForRound(YawTowardCenter(pos));

            _alive.Add(id);
            _fighters[id] = fighter;
        }

        Announce("GRAB YOUR SWORDS");
        StartCoroutine(CountdownThenFight());
        Debug.Log($"[DrunkSwordfight] started with {_alive.Count} fighters.");
    }

    private IEnumerator CountdownThenFight()
    {
        yield return new WaitForSeconds(0.6f);

        Announce("3"); Beep(440f);  yield return new WaitForSeconds(countdownStep);
        Announce("2"); Beep(440f);  yield return new WaitForSeconds(countdownStep);
        Announce("1"); Beep(440f);  yield return new WaitForSeconds(countdownStep);

        Announce("FIGHT!"); Beep(880f);
        foreach (var fighter in _fighters.Values) fighter.HostSetFrozen(false);
        _fightStarted = true;
        _nextRoundAt = Time.time + roundIntervalSeconds;
    }

    protected override void Update()
    {
        base.Update();                       // time-limit safety net
        if (!IsServer || !_fightStarted || _roundOver) return;

        // "another round on the house": everyone gets drunker over time, so
        // camping the last two sips is not a strategy
        if (roundIntervalSeconds > 0f && Time.time >= _nextRoundAt)
        {
            _nextRoundAt = Time.time + roundIntervalSeconds;
            bool anyoneLeveled = false;
            foreach (var kv in _fighters)
            {
                var fighter = kv.Value;
                if (fighter == null || !fighter.Alive.Value || fighter.DrunkLevel.Value >= 3) continue;
                fighter.DrunkLevel.Value++;
                anyoneLeveled = true;
            }
            if (anyoneLeveled)
            {
                Announce("ANOTHER ROUND ON THE HOUSE — +1 DRUNK");
                Beep(660f);
                BeepAfter(0.15f, 880f);
            }
        }

        for (int i = _alive.Count - 1; i >= 0; i--)
        {
            var id = _alive[i];

            if (!NetworkManager.ConnectedClients.TryGetValue(id, out var client) ||
                client.PlayerObject == null)
            {
                Eliminate(id, $"{NameOf(id)} STUMBLED OUT");
                continue;
            }

            var fighter = client.PlayerObject.GetComponent<DrunkFightPlayer>();
            if (fighter == null || !fighter.Alive.Value)
            {
                Eliminate(id, $"{NameOf(id)} IS DOWN!");
                continue;
            }

            if (fighter.transform.position.y < fallKillHeight)
                fighter.HostTakeDamage(999f, Vector3.up, NetworkManager.ServerClientId);
        }

        if (_alive.Count <= 1) FinishRound();
    }

    private void Eliminate(ulong id, string message)
    {
        if (!_alive.Remove(id)) return;
        _deadOrder.Add(id);
        Announce(message);
        Debug.Log($"[DrunkSwordfight] {message}");
    }

    private void FinishRound()
    {
        _roundOver = true;

        var placements = new List<ulong>(_alive);
        for (int i = _deadOrder.Count - 1; i >= 0; i--) placements.Add(_deadOrder[i]);

        if (_alive.Count == 1)
            Announce($"{NameOf(_alive[0])} WINS!");
        else
            Announce("EVERBODY'S ON THE FLOOR");

        FanfareClientRpc();

        StartCoroutine(EndAfter(winnerLinger, placements.ToArray()));
    }

    private IEnumerator EndAfter(float delay, ulong[] placements)
    {
        yield return new WaitForSeconds(delay);
        EndMinigame(placements);
    }

    protected override ulong[] GetPlacementsOnTimeout()
    {
        // survivors first (healthiest first), then the eliminated in reverse
        var placements = new List<ulong>(_alive);
        placements.Sort((a, b) =>
        {
            float ha = _fighters.TryGetValue(a, out var fa) && fa != null ? fa.Hp.Value : -1f;
            float hb = _fighters.TryGetValue(b, out var fb) && fb != null ? fb.Hp.Value : -1f;
            return hb.CompareTo(ha);
        });
        for (int i = _deadOrder.Count - 1; i >= 0; i--) placements.Add(_deadOrder[i]);
        return placements.ToArray();
    }

    private void Announce(string text) => Announcement.Value = new FixedString64Bytes(text);
    private string NameOf(ulong id) =>
        _fighters.TryGetValue(id, out var f) && f != null ? f.FighterName.Value.ToString() : $"Player {id}";

    private static float YawTowardCenter(Vector3 from)
    {
        var dir = new Vector3(-from.x, 0f, -from.z);
        if (dir.sqrMagnitude < 0.01f) return 0f;
        return Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
    }

    private void Beep(float freq)
    {
        if (_music != null) _music.PlayOneShot(ProceduralAudio.Beep(freq), 0.5f);
    }

    private void BeepAfter(float delay, float freq)
    {
        StartCoroutine(BeepAfterRoutine(delay, freq));
    }

    private IEnumerator BeepAfterRoutine(float delay, float freq)
    {
        yield return new WaitForSeconds(delay);
        Beep(freq);
    }

    [ClientRpc]
    private void FanfareClientRpc()
    {
        if (_music != null) _music.PlayOneShot(ProceduralAudio.Fanfare(), 0.6f);
    }
}

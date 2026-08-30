using Unity.Netcode;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// The one movement rule for the whole project: the client sends INTENT,
/// the host MOVES. Nobody writes to their own transform.
///
/// Goes on the player avatar prefab, together with:
///   Rigidbody, Collider, NetworkObject, NetworkTransform (server authoritative).
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class MinigamePlayerController : NetworkBehaviour
{
    [SerializeField] private float moveSpeed = 6f;

    private Rigidbody _rb;
    private Vector2 _hostInput;   // host side: latest intent from this client
    private Vector2 _lastSent;    // owner side: don't spam identical input
    private Vector2? _moveOverride; // owner side: one-frame camera-relative input from another component

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _rb.freezeRotation = true;
    }

    /// <summary>
    /// Optional: another component (e.g. DrunkFightPlayer) can redirect the
    /// WASD intent, camera-relative. Set every frame; null falls back to raw
    /// keyboard axes.
    /// </summary>
    public void SetMoveInputOverride(Vector2? input) => _moveOverride = input;

    private void Update()
    {
        if (!IsOwner) return;

        var input = _moveOverride ?? ReadInput();
        _moveOverride = null;
        if (input == _lastSent) return;

        _lastSent = input;
        SendInputServerRpc(input);
    }

    private void FixedUpdate()
    {
        if (!IsServer) return;

        var wanted = new Vector3(_hostInput.x, 0f, _hostInput.y) * moveSpeed;
        var v = _rb.linearVelocity;
        _rb.linearVelocity = new Vector3(wanted.x, v.y, wanted.z);
    }

    [ServerRpc]
    private void SendInputServerRpc(Vector2 input)
    {
        // Never trust the client's magnitude.
        _hostInput = Vector2.ClampMagnitude(input, 1f);
    }

    private static Vector2 ReadInput()
    {
#if ENABLE_INPUT_SYSTEM
        return ReadWasd();
#else
        return new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
#endif
    }

    /// <summary>Raw WASD intent in [-1,1] on both axes (new Input System).</summary>
    public static Vector2 ReadWasd()
    {
        var kb = Keyboard.current;
        if (kb == null) return Vector2.zero;
        float x = (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f);
        float y = (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f);
        return new Vector2(x, y);
    }
}

using UnityEngine;

/// <summary>
/// Drives the character model's Idle/Walking blend from observed motion.
/// Runs on every peer: the host simulates the rigidbody, clients see the
/// NetworkTransform interpolation — both can measure position deltas.
/// </summary>
public class AvatarAnimatorDriver : MonoBehaviour
{
    [SerializeField] private float walkSpeedReference = 5f;

    private Animator _animator;
    private Vector3 _lastPos;

    private void Awake()
    {
        _animator = GetComponentInChildren<Animator>();
        _lastPos = transform.position;
    }

    private void Update()
    {
        if (_animator == null) return;

        var vel = (transform.position - _lastPos) / Mathf.Max(Time.deltaTime, 1e-4f);
        _lastPos = transform.position;

        float speed = Mathf.Clamp01(new Vector2(vel.x, vel.z).magnitude / walkSpeedReference);
        _animator.SetFloat("Speed", Mathf.Lerp(_animator.GetFloat("Speed"), speed, 0.25f));
    }
}

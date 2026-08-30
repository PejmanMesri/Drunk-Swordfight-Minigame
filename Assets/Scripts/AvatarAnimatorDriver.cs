using System.Collections;
using UnityEngine;

/// <summary>
/// Drives the character model's Idle/Walking blend from observed motion.
/// Runs on every peer: the host simulates the rigidbody, clients see the
/// NetworkTransform interpolation — both can measure position deltas.
///
/// Also self-calibrates the model's height once at spawn: bind-pose bounds
/// (what we can measure offline) can sit far from where the RETARGETED
/// runtime animation actually puts the feet. We wait for the first animated
/// frames, then slide the "Model" child so the feet land at the capsule's
/// bottom (local y = -1, i.e. ground level).
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

    private void Start()
    {
        StartCoroutine(CalibrateModelToGround());
    }

    private IEnumerator CalibrateModelToGround()
    {
        // let the animator apply a couple of frames of the real pose first
        yield return null;
        yield return null;

        var model = transform.Find("Model");
        if (model == null || _animator == null) yield break;

        float footY;
        var lFoot = _animator.GetBoneTransform(HumanBodyBones.LeftFoot);
        var rFoot = _animator.GetBoneTransform(HumanBodyBones.RightFoot);
        if (lFoot != null && rFoot != null)
            footY = Mathf.Min(lFoot.position.y, rFoot.position.y);
        else
        {
            var smr = model.GetComponentInChildren<SkinnedMeshRenderer>();
            if (smr == null) yield break;
            footY = smr.bounds.min.y;
        }

        float desired = transform.position.y - 1f;      // capsule bottom
        model.position += Vector3.up * (desired - footY);
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

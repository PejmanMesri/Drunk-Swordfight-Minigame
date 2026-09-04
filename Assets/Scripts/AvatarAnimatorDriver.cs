using System.Collections;
using UnityEngine;

/// <summary>
/// Drives the character model's Idle/Walking blend from observed motion, and
/// self-calibrates the model's height once at spawn (bind-pose bounds can
/// sit far from where the retargeted runtime animation actually puts the
/// feet — we slide the "Model" child until the feet land on the ground).
///
/// During the swordfight it also runs right-hand IK onto the sword grip, so
/// the arm visibly holds the blade and swings with it. The Base Layer of
/// PlayerController has IK Pass enabled for this.
/// </summary>
public class AvatarAnimatorDriver : MonoBehaviour
{
    [SerializeField] private float walkSpeedReference = 5f;

    private Animator _animator;
    private Vector3 _lastPos;

    // sword IK (set by DrunkFightPlayer when the fight gear activates)
    private DrunkFightPlayer _swordOwner;
    private SwordWielder _sword;
    private float _ikWeight;

    /// <summary>Attach the right hand to this fighter's sword grip.</summary>
    public void SetSwordGrip(DrunkFightPlayer owner, SwordWielder sword)
    {
        _swordOwner = owner;
        _sword = sword;
    }

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

    private void OnAnimatorIK(int layerIndex)
    {
        if (_animator == null) return;

        bool grip = _sword != null && _swordOwner != null
            && _swordOwner.GearActive && _swordOwner.Alive.Value;

        _ikWeight = Mathf.MoveTowards(_ikWeight, grip ? 1f : 0f, Time.deltaTime * 6f);
        if (_ikWeight < 0.01f)
        {
            _animator.SetIKPositionWeight(AvatarIKGoal.RightHand, 0f);
            _animator.SetIKRotationWeight(AvatarIKGoal.RightHand, 0f);
            _animator.SetIKHintPositionWeight(AvatarIKHint.RightElbow, 0f);
            return;
        }

        var hand = _sword.HandTarget;
        var hint = _sword.ElbowHint;

        _animator.SetIKPosition(AvatarIKGoal.RightHand, hand.position);
        _animator.SetIKRotation(AvatarIKGoal.RightHand, hand.rotation);
        _animator.SetIKHintPosition(AvatarIKHint.RightElbow, hint.position);

        _animator.SetIKPositionWeight(AvatarIKGoal.RightHand, _ikWeight);
        // position dominates; rotation at partial weight so a bad bone axis
        // doesn't snap the wrist into spaghetti
        _animator.SetIKRotationWeight(AvatarIKGoal.RightHand, _ikWeight * 0.6f);
        _animator.SetIKHintPositionWeight(AvatarIKHint.RightElbow, _ikWeight * 0.8f);
    }
}

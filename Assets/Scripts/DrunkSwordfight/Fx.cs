using UnityEngine;

/// <summary>Small runtime effects. Currently: hit sparks.</summary>
public static class Fx
{
    private static Material _sparkMat;
    private static int _activeSparks;

    public static void Sparks(Vector3 point, Vector3 dir, float amount)
    {
        if (_activeSparks > 24) return;   // don't spam the scene

        var go = new GameObject("Sparks");
        go.transform.position = point;
        go.transform.rotation = Quaternion.LookRotation(
            dir.sqrMagnitude > 0.001f ? dir : Vector3.up);

        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);   // configure while stopped

        var main = ps.main;
        main.playOnAwake = false;
        main.duration = 0.3f;
        main.loop = false;
        main.startLifetime = 0.15f + amount * 0.004f;
        main.startSpeed = 3.5f + amount * 0.12f;
        main.startSize = 0.03f;
        main.startColor = new Color(1f, 0.65f, 0.2f);
        main.gravityModifier = 0.8f;
        main.playOnAwake = true;
        main.loop = false;

        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, Mathf.Min((short)(amount + 4), (short)24)) });

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 35f;

        if (_sparkMat == null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader != null) _sparkMat = new Material(shader);
        }
        if (_sparkMat != null)
        {
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.material = _sparkMat;
        }

        _activeSparks++;
        Object.Destroy(go, 1.2f);

        var watcher = go.AddComponent<FxSparkCounter>();
        watcher.OnDead = () => _activeSparks--;

        ps.Play();
    }

    private class FxSparkCounter : MonoBehaviour
    {
        public System.Action OnDead;
        private void OnDestroy() => OnSafe();
        private void OnSafe() => OnDead?.Invoke();
    }
}

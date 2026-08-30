using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Tiny shared helpers for the drunk swordfight minigame.</summary>
public static class MathUtil
{
    /// <summary>Wraps an angle into (-180, 180].</summary>
    public static float NormalizeAngleDeg(float deg)
    {
        deg %= 360f;
        if (deg > 180f) deg -= 360f;
        else if (deg <= -180f) deg += 360f;
        return deg;
    }

    /// <summary>Rotates a 2D vector by deg degrees (useful for camera-relative input).</summary>
    public static Vector2 Rotate(Vector2 v, float deg)
    {
        float r = deg * Mathf.Deg2Rad;
        float c = Mathf.Cos(r), s = Mathf.Sin(r);
        return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
    }
}

public static class SceneUtil
{
    /// <summary>True if a scene with this name is currently loaded (any mode).</summary>
    public static bool IsSceneLoaded(string sceneName)
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).name == sceneName)
                return true;
        return false;
    }
}

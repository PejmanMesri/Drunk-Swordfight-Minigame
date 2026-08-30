using UnityEngine;

/// <summary>
/// Procedural whiskey bottle: hangs on the belt until you sip (Q), then
/// animates up to your face and back. Three sips, one bottle — after that
/// it's gone and you're on your own.
/// </summary>
public static class WhiskeyBottle
{
    public static Transform Build(Transform parent, Vector3 localPos)
    {
        var glass = SwordWielder.LoadMat("DrunkFight/Mats/WhiskeyGlass");
        var cork = SwordWielder.LoadMat("DrunkFight/Mats/Cork");
        var label = SwordWielder.LoadMat("DrunkFight/Mats/WhiskeyLabel");

        var go = new GameObject("WhiskeyBottle");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.Euler(0f, 0f, 18f);

        Part(go.transform, "Body", PrimitiveType.Cylinder, new Vector3(0.11f, 0.115f, 0.11f),
            new Vector3(0f, 0f, 0f), glass);
        Part(go.transform, "Shoulder", PrimitiveType.Cylinder, new Vector3(0.065f, 0.03f, 0.065f),
            new Vector3(0f, 0.14f, 0f), glass);
        Part(go.transform, "Neck", PrimitiveType.Cylinder, new Vector3(0.032f, 0.05f, 0.032f),
            new Vector3(0f, 0.18f, 0f), glass);
        Part(go.transform, "Cork", PrimitiveType.Cylinder, new Vector3(0.036f, 0.018f, 0.036f),
            new Vector3(0f, 0.235f, 0f), cork);
        Part(go.transform, "Label", PrimitiveType.Cylinder, new Vector3(0.117f, 0.05f, 0.117f),
            new Vector3(0f, 0f, 0f), label);

        return go.transform;
    }

    private static void Part(Transform parent, string name, PrimitiveType type, Vector3 scale,
                             Vector3 localPos, Material mat)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        Object.Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localScale = scale;
        go.transform.localPosition = localPos;
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
    }
}

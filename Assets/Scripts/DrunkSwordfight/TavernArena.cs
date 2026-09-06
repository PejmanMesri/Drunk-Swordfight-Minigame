using UnityEngine;

/// <summary>
/// Builds the tavern deterministically in Awake on EVERY peer (host and
/// clients) — same layout everywhere, so it needs no networking and no
/// scene authoring. All primitives plus materials wired in the editor step
/// (with colored fallbacks so the scene still works standalone).
///
/// Layout keeps a clear brawl zone in the middle; furniture is collider'd
/// so you can be cornered against the bar. That's a feature.
/// </summary>
public class TavernArena : MonoBehaviour
{
    [Header("Materials (wired by the editor setup, fallbacks if empty)")]
    [SerializeField] private Material woodFloor;
    [SerializeField] private Material woodWall;
    [SerializeField] private Material plainWood;
    [SerializeField] private Material stone;
    [SerializeField] private Material metalDark;
    [SerializeField] private Material glassAmber;

    private static readonly Vector2 FloorSize = new(14f, 14f);
    private const float WallHeight = 3.6f;

    private void Awake()
    {
        EnsureMaterials();
        BuildFloorAndWalls();
        BuildBar();
        BuildTables();
        BuildBarrels();
        BuildCrates();
        BuildLights();
        BuildDustMotes();
    }

    // ------------------------------------------------------------------ pieces

    private void BuildFloorAndWalls()
    {
        var floor = Box("Floor", new Vector3(0, -0.2f, 0), new Vector3(FloorSize.x, 0.4f, FloorSize.y), woodFloor);

        var half = FloorSize * 0.5f;
        Box("WallN", new Vector3(0, WallHeight / 2f, -half.y - 0.2f), new Vector3(FloorSize.x + 0.8f, WallHeight, 0.4f), stone);
        Box("WallS", new Vector3(0, WallHeight / 2f, half.y + 0.2f), new Vector3(FloorSize.x + 0.8f, WallHeight, 0.4f), stone);
        Box("WallW", new Vector3(-half.x - 0.2f, WallHeight / 2f, 0), new Vector3(0.4f, WallHeight, FloorSize.y), stone);
        Box("WallE", new Vector3(half.x + 0.2f, WallHeight / 2f, 0), new Vector3(0.4f, WallHeight, FloorSize.y), stone);

        // wood trim along the bottom of the walls
        Box("TrimN", new Vector3(0, 0.55f, -half.y + 0.08f), new Vector3(FloorSize.x, 1.1f, 0.16f), woodWall);
        Box("TrimS", new Vector3(0, 0.55f, half.y - 0.08f), new Vector3(FloorSize.x, 1.1f, 0.16f), woodWall);
        Box("TrimW", new Vector3(-half.x + 0.08f, 0.55f, 0), new Vector3(0.16f, 1.1f, FloorSize.y), woodWall);
        Box("TrimE", new Vector3(half.x - 0.08f, 0.55f, 0), new Vector3(0.16f, 1.1f, FloorSize.y), woodWall);

        // corner pillars
        foreach (var corner in new[] {
                     new Vector3(-5.4f, 0, -5.4f), new Vector3(5.4f, 0, -5.4f),
                     new Vector3(-5.4f, 0, 5.4f), new Vector3(5.4f, 0, 5.4f) })
            Box($"Pillar{corner.x:0;#}{corner.z:0;#}", corner + new Vector3(0, WallHeight / 2f, 0),
                new Vector3(0.5f, WallHeight, 0.5f), plainWood);
    }

    private void BuildBar()
    {
        Box("BarBase", new Vector3(0, 0.525f, -6.0f), new Vector3(10f, 1.05f, 0.7f), plainWood);
        Box("BarTop", new Vector3(0, 1.10f, -6.0f), new Vector3(10.4f, 0.1f, 0.9f), woodWall);

        // back shelf with a row of glowing bottles (it's that kind of tavern)
        Box("Shelf", new Vector3(0, 1.55f, -6.75f), new Vector3(10f, 0.08f, 0.35f), plainWood);
        for (int i = 0; i < 16; i++)
        {
            var bottle = Cyl($"ShelfBottle{i}", new Vector3(-4.7f + i * 0.625f, 1.72f, -6.75f),
                new Vector2(0.05f, 0.26f), glassAmber);
            Object.Destroy(bottle.GetComponent<Collider>());   // decorative only
        }
    }

    private void BuildTables()
    {
        Table(new Vector3(0, 0, -2.8f));
        Table(new Vector3(-3.2f, 0, 1.9f));
        Table(new Vector3(3.2f, 0, 1.9f));
    }

    private void Table(Vector3 center)
    {
        Cyl($"TableTop@{center.x},{center.z}", center + new Vector3(0, 0.95f, 0), new Vector2(0.9f, 0.08f), plainWood);
        Cyl($"TableLeg@{center.x},{center.z}", center + new Vector3(0, 0.475f, 0), new Vector2(0.09f, 0.95f), plainWood);
        Cyl($"TableFoot@{center.x},{center.z}", center + new Vector3(0, 0.04f, 0), new Vector2(0.4f, 0.08f), plainWood);

        for (int i = 0; i < 3; i++)
        {
            float a = i * (Mathf.PI * 2f / 3f) + 0.5f;
            var pos = center + new Vector3(Mathf.Cos(a) * 1.45f, 0, Mathf.Sin(a) * 1.45f);
            Cyl($"Stool@{pos.x:0.#},{pos.z:0.#}", pos + new Vector3(0, 0.31f, 0), new Vector2(0.06f, 0.62f), plainWood);
            Cyl($"StoolSeat@{pos.x:0.#},{pos.z:0.#}", pos + new Vector3(0, 0.66f, 0), new Vector2(0.3f, 0.09f), woodWall);
        }
    }

    private void BuildBarrels()
    {
        Barrel(new Vector3(-6.1f, 0, 4.9f));
        Barrel(new Vector3(6.1f, 0, 4.9f));
        Barrel(new Vector3(-6.1f, 0, 3.8f));
        Barrel(new Vector3(6.35f, 0, -1.2f));
        Barrel(new Vector3(-2.2f, 0, -6.3f));
    }

    private void Barrel(Vector3 pos)
    {
        var barrel = Cyl($"Barrel@{pos.x:0.#},{pos.z:0.#}", pos + new Vector3(0, 0.525f, 0),
            new Vector2(0.42f, 1.05f), plainWood);

        foreach (float dy in new[] { -0.28f, 0.28f })
        {
            var ring = Cyl("Ring", pos + new Vector3(0, 0.525f + dy, 0), new Vector2(0.435f, 0.06f), metalDark);
            ring.transform.SetParent(barrel.transform, true);
            Object.Destroy(ring.GetComponent<Collider>());
        }
    }

    private void BuildCrates()
    {
        Box("Crate1", new Vector3(6.0f, 0.35f, 0.4f), Vector3.one * 0.7f, plainWood);
        Box("Crate2", new Vector3(6.0f, 1.05f, 0.55f), Vector3.one * 0.65f, plainWood);
        Box("Crate3", new Vector3(-6.1f, 0.35f, -1.3f), Vector3.one * 0.7f, plainWood);
        Box("Crate4", new Vector3(-5.4f, 0.35f, -1.75f), Vector3.one * 0.6f, plainWood);
    }

    private void BuildLights()
    {
        // dim cool "moonlight" so silhouettes read; the warm points do the work
        var moonGo = new GameObject("TavernMoonlight");
        moonGo.transform.SetParent(transform, false);
        moonGo.transform.rotation = Quaternion.Euler(55f, -35f, 0f);
        var moon = moonGo.AddComponent<Light>();
        moon.type = LightType.Directional;
        moon.color = new Color(0.5f, 0.6f, 0.9f);
        moon.intensity = 0.4f;
        moon.shadows = LightShadows.Soft;

        Point(new Vector3(0, 3.0f, 0.5f), 14f, 2.6f);         // center of the brawl
        Point(new Vector3(0, 2.3f, -5.9f), 8f, 1.8f);         // over the bar
        Point(new Vector3(-5.0f, 2.3f, 5.0f), 8f, 1.5f);      // corner sconces
        Point(new Vector3(5.0f, 2.3f, 5.0f), 8f, 1.5f);
    }

    private void Point(Vector3 pos, float range, float intensity)
    {
        var go = new GameObject($"Lantern@{pos.x:0.#},{pos.z:0.#}");
        go.transform.SetParent(transform, false);
        go.transform.position = pos;

        // little glowing lantern body so the light source is visible
        Cyl("LanternBody", pos, new Vector2(0.09f, 0.22f), metalDark).transform.SetParent(go.transform, true);
        var glow = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        glow.name = "LanternGlow";
        Object.Destroy(glow.GetComponent<Collider>());
        glow.transform.SetParent(go.transform, true);
        glow.transform.localPosition = Vector3.zero;
        glow.transform.localScale = Vector3.one * 0.09f;
        glow.GetComponent<MeshRenderer>().sharedMaterial = glassAmber;

        var light = go.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(1f, 0.72f, 0.38f);
        light.range = range;
        light.intensity = intensity;
        light.shadows = LightShadows.Soft;

        go.AddComponent<LanternFlicker>();       // candlelight, not fluorescent
    }

    private void BuildDustMotes()
    {
        var go = new GameObject("DustMotes");
        go.transform.SetParent(transform, false);
        go.transform.position = new Vector3(0f, 2.4f, 0f);

        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.playOnAwake = true;
        main.loop = true;
        main.duration = 6f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(4f, 7f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.02f, 0.08f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.015f, 0.04f);
        main.startColor = new Color(1f, 0.9f, 0.7f, 0.16f);
        main.gravityModifier = -0.005f;          // lazily float upward
        main.maxParticles = 80;

        var emission = ps.emission;
        emission.rateOverTime = 12f;

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(12f, 2.4f, 12f);

        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.x = new ParticleSystem.MinMaxCurve(-0.06f, 0.06f);
        vel.z = new ParticleSystem.MinMaxCurve(-0.06f, 0.06f);

        var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader != null)
        {
            var mat = new Material(shader);
            mat.SetFloat("_Surface", 1f);        // transparent
            mat.SetFloat("_Blend", 0f);
            mat.renderQueue = 3000;
            go.GetComponent<ParticleSystemRenderer>().material = mat;
        }

        ps.Play();
    }

    // ------------------------------------------------------------------ helpers

    private GameObject Box(string name, Vector3 pos, Vector3 size, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(transform, false);
        go.transform.position = pos;
        go.transform.localScale = size;
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        return go;
    }

    private GameObject Cyl(string name, Vector3 pos, Vector2 radiusHeight, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = name;
        go.transform.SetParent(transform, false);
        go.transform.position = pos;
        go.transform.localScale = new Vector3(radiusHeight.x * 2f, radiusHeight.y, radiusHeight.x * 2f);
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        return go;
    }

    private void EnsureMaterials()
    {
        var lit = Shader.Find("Universal Render Pipeline/Lit");
        woodFloor ??= NewMat(lit, new Color(0.42f, 0.27f, 0.15f));
        woodWall ??= NewMat(lit, new Color(0.30f, 0.19f, 0.11f));
        plainWood ??= NewMat(lit, new Color(0.47f, 0.33f, 0.19f));
        stone ??= NewMat(lit, new Color(0.36f, 0.35f, 0.38f));
        metalDark ??= NewMat(lit, new Color(0.15f, 0.15f, 0.17f));
        glassAmber ??= NewMat(lit, new Color(1f, 0.6f, 0.2f), emissive: new Color(1f, 0.5f, 0.15f) * 1.4f);
    }

    private static Material NewMat(Shader lit, Color color, Color? emissive = null)
    {
        var m = new Material(lit);
        m.color = color;
        if (emissive != null)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", emissive.Value);
        }
        return m;
    }
}

/// <summary>
/// Candlelight flicker: two octaves of perlin noise wandering the intensity,
/// so the tavern breathes instead of sitting under office lighting.
/// </summary>
public class LanternFlicker : MonoBehaviour
{
    private Light _light;
    private float _base;
    private float _seed;

    private void Awake()
    {
        _light = GetComponent<Light>();
        _base = _light != null ? _light.intensity : 1f;
        _seed = Random.value * 100f;
    }

    private void Update()
    {
        if (_light == null) return;
        float t = Time.time;
        float f = 1f
            + (Mathf.PerlinNoise(t * 7.0f, _seed) - 0.5f) * 0.22f     // fast crackle
            + (Mathf.PerlinNoise(t * 1.7f, _seed + 40f) - 0.5f) * 0.12f; // slow swell
        _light.intensity = _base * f;
    }
}

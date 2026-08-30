using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Owner-only screen HUD, built entirely from code (no canvas authoring):
/// big HP bar, the whiskey bottle with its three sips, drunk-o-meter,
/// center announcements (countdown / kills / winner), hit flash, sip flash
/// and a controls hint that fades away.
/// </summary>
public class FighterHud : MonoBehaviour
{
    private DrunkFightPlayer _owner;

    private Text _nameText;
    private Image _hpFill;
    private RectTransform _hpFillRect;
    private Text _hpText;
    private Image[] _sipPips;
    private Text _drunkText;
    private Text _announce;
    private Image _damageFlash;
    private Image _sipFlash;
    private Text _sipPopup;
    private Text _hint;

    private float _damageAlpha;
    private float _sipAlpha;
    private float _hintTimer = 8f;

    private static readonly string[] DrunkNames = { "SOBER", "BUZZED", "TIPSY", "WASTED" };
    private static readonly Color[] DrunkColors =
    {
        new(0.7f, 0.9f, 0.7f), new(1f, 0.85f, 0.3f), new(1f, 0.55f, 0.2f), new(1f, 0.3f, 0.25f)
    };

    public static FighterHud Create(DrunkFightPlayer owner)
    {
        var go = new GameObject("DrunkFightHud");
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        var hud = go.AddComponent<FighterHud>();
        hud._owner = owner;
        hud.Build();
        return hud;
    }

    private void Build()
    {
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        // ---- bottom left: name + HP ------------------------------------
        _nameText = Text(font, 28, new Vector2(330, 96), new Vector2(600, 40),
            new Vector2(0f, 0f), TextAnchor.MiddleCenter, new Color(1f, 0.92f, 0.7f));

        var hpBg = Image(new Color(0f, 0f, 0f, 0.6f), new Vector2(330, 48), new Vector2(560, 32),
            new Vector2(0f, 0f));
        _hpFill = Image(new Color(0.25f, 0.8f, 0.25f), Vector2.zero, Vector2.zero, new Vector2(0f, 0f));
        _hpFillRect = (RectTransform)_hpFill.transform;
        _hpFillRect.SetParent(hpBg.rectTransform, false);
        _hpFillRect.anchorMin = Vector2.zero;
        _hpFillRect.anchorMax = Vector2.one;
        _hpFillRect.offsetMin = new Vector2(4, 4);
        _hpFillRect.offsetMax = new Vector2(-4, -4);

        _hpText = Text(font, 18, new Vector2(330, 48), new Vector2(560, 32),
            new Vector2(0f, 0f), TextAnchor.MiddleCenter, Color.white);

        // ---- bottom right: whiskey + sips + drunk meter -----------------
        Image(new Color(0.55f, 0.22f, 0.05f, 0.95f), new Vector2(-70, 62), new Vector2(44, 100), new Vector2(1f, 0f));
        Image(new Color(0.55f, 0.22f, 0.05f, 0.95f), new Vector2(-70, 126), new Vector2(16, 30), new Vector2(1f, 0f));
        Image(new Color(0.66f, 0.51f, 0.36f, 0.95f), new Vector2(-70, 156), new Vector2(20, 12), new Vector2(1f, 0f));
        Image(new Color(0.95f, 0.9f, 0.75f, 0.9f), new Vector2(-70, 48), new Vector2(48, 26), new Vector2(1f, 0f));

        _sipPips = new Image[3];
        for (int i = 0; i < 3; i++)
        {
            _sipPips[i] = Image(new Color(1f, 0.62f, 0.15f),
                new Vector2(-28, 28 + i * 34), new Vector2(22, 22), new Vector2(1f, 0f));
        }
        Text(font, 22, new Vector2(-40, 168), new Vector2(300, 30), new Vector2(1f, 0f),
            TextAnchor.LowerRight, new Color(1f, 0.92f, 0.7f)).text = "Q — SIP";

        _drunkText = Text(font, 30, new Vector2(-40, 200), new Vector2(400, 40), new Vector2(1f, 0f),
            TextAnchor.LowerRight, DrunkColors[0]);

        // ---- top center: announcements -----------------------------------
        _announce = Text(font, 56, new Vector2(0, -80), new Vector2(1500, 90), new Vector2(0.5f, 1f),
            TextAnchor.UpperCenter, new Color(1f, 0.85f, 0.4f));

        // ---- crosshair ----------------------------------------------------
        Image(new Color(1f, 1f, 1f, 0.7f), Vector2.zero, new Vector2(6, 6), new Vector2(0.5f, 0.5f));

        // ---- full-screen flashes ------------------------------------------
        _damageFlash = Image(new Color(0.8f, 0.05f, 0f, 0f), Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));
        Stretch(_damageFlash.rectTransform);
        _sipFlash = Image(new Color(1f, 0.75f, 0.25f, 0f), Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));
        Stretch(_sipFlash.rectTransform);

        _sipPopup = Text(font, 40, new Vector2(0, -300), new Vector2(500, 60), new Vector2(0.5f, 1f),
            TextAnchor.UpperCenter, new Color(0.6f, 1f, 0.5f, 0f));
        _sipPopup.text = "+35 HP ... and a bit more drunk";

        // ---- controls hint --------------------------------------------------
        _hint = Text(font, 26, new Vector2(0, 40), new Vector2(1200, 40), new Vector2(0.5f, 0f),
            TextAnchor.UpperCenter, new Color(1f, 1f, 1f, 0.8f));
        _hint.text = "WASD move  ·  MOUSE wields the sword  ·  LMB stab  ·  Q sip whiskey  ·  cross blades to block";
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private Text Text(Font font, int fontSize, Vector2 pos, Vector2 size, Vector2 anchor,
                      TextAnchor align, Color color)
    {
        var go = new GameObject("Text");
        go.transform.SetParent(transform, false);
        var text = go.AddComponent<Text>();
        text.font = font;
        text.fontSize = fontSize;
        text.alignment = align;
        text.color = color;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        var rt = text.rectTransform;
        rt.anchorMin = rt.anchorMax = anchor;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        return text;
    }

    private Image Image(Color color, Vector2 pos, Vector2 size, Vector2 anchor)
    {
        var go = new GameObject("Image");
        go.transform.SetParent(transform, false);
        var img = go.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        var rt = img.rectTransform;
        rt.anchorMin = rt.anchorMax = anchor;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        return img;
    }

    // ---------------------------------------------------------------- feedback

    public void FlashDamage(float amount)
    {
        _damageAlpha = Mathf.Max(_damageAlpha, Mathf.Clamp01(amount / 35f) * 0.7f);
    }

    public void FlashSip()
    {
        _sipAlpha = 0.35f;
    }

    private void Update()
    {
        if (_owner == null) return;

        _nameText.text = _owner.FighterName.Value.ToString();

        float frac = Mathf.Clamp01(_owner.Hp.Value / _owner.MaxHp);
        _hpFillRect.anchorMax = new Vector2(Mathf.Max(frac, 0.001f), 1f);
        _hpFill.color = Color.Lerp(new Color(0.85f, 0.15f, 0.1f), new Color(0.25f, 0.8f, 0.25f), frac);
        _hpText.text = $"{_owner.Hp.Value:0} / {_owner.MaxHp:0}";

        for (int i = 0; i < 3; i++)
            _sipPips[i].color = i < _owner.SipsLeft.Value
                ? new Color(1f, 0.62f, 0.15f) : new Color(1f, 1f, 1f, 0.15f);

        int drunk = _owner.DrunkLevel.Value;
        _drunkText.text = DrunkNames[Mathf.Clamp(drunk, 0, 3)];
        _drunkText.color = DrunkColors[Mathf.Clamp(drunk, 0, 3)];

        var mg = DrunkSwordfightMinigame.Instance;
        if (mg != null) _announce.text = mg.Announcement.Value.ToString();

        if (_damageAlpha > 0f)
        {
            _damageFlash.color = new Color(0.8f, 0.05f, 0f, _damageAlpha);
            _damageAlpha = Mathf.MoveTowards(_damageAlpha, 0f, Time.deltaTime * 1.4f);
        }
        if (_sipAlpha > 0f)
        {
            _sipFlash.color = new Color(1f, 0.75f, 0.25f, _sipAlpha);
            _sipAlpha = Mathf.MoveTowards(_sipAlpha, 0f, Time.deltaTime * 0.8f);
            _sipPopup.color = new Color(0.6f, 1f, 0.5f, _sipAlpha * 2f);
        }

        if (_hintTimer > 0f)
        {
            _hintTimer -= Time.deltaTime;
            if (_hintTimer < 2f)
                _hint.color = new Color(1f, 1f, 1f, 0.8f * (_hintTimer / 2f));
        }
        else if (_hint != null) _hint.text = "";
    }
}

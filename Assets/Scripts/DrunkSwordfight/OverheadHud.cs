using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Floating billboard above each fighter: name, HP bar, sips left and how
/// drunk they are. Runs on every peer and reads the NetworkVariables, so it
/// stays in sync for free.
///
/// Heights are ROOT-LOCAL: the avatar root is the capsule center, feet at
/// -1, head around +0.75 — so the bar lives just above the head at +1.05.
/// </summary>
public class OverheadHud : MonoBehaviour
{
    private DrunkFightPlayer _owner;
    private Text _name;
    private Image _hpFill;
    private RectTransform _hpFillRect;
    private Image[] _sipPips;
    private Image[] _drunkPips;
    private CanvasGroup _group;

    private static Camera _camCache;
    private static float _camRefreshAt;

    public static OverheadHud Create(DrunkFightPlayer owner)
    {
        var go = new GameObject("OverheadHud");
        go.transform.SetParent(owner.transform, false);
        go.transform.localPosition = new Vector3(0f, 1.05f, 0f);

        var hud = go.AddComponent<OverheadHud>();
        hud._owner = owner;
        hud.Build();
        return hud;
    }

    private void Build()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = null;

        _group = gameObject.AddComponent<CanvasGroup>();

        var root = (RectTransform)transform;
        root.sizeDelta = new Vector2(0.95f, 0.30f);
        root.localScale = Vector3.one;

        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        _name = MakeText(font, 14, TextAnchor.LowerCenter);
        _name.rectTransform.anchorMin = _name.rectTransform.anchorMax = new Vector2(0.5f, 1f);
        _name.rectTransform.sizeDelta = new Vector2(0.95f, 0.13f);
        _name.rectTransform.anchoredPosition = Vector2.zero;
        _name.color = new Color(1f, 0.93f, 0.72f);

        var bg = MakeImage(new Color(0f, 0f, 0f, 0.65f));
        bg.rectTransform.anchorMin = bg.rectTransform.anchorMax = new Vector2(0.5f, 1f);
        bg.rectTransform.sizeDelta = new Vector2(0.72f, 0.075f);
        bg.rectTransform.anchoredPosition = new Vector2(0f, -0.20f);

        _hpFill = MakeImage(new Color(0.85f, 0.2f, 0.15f));
        _hpFillRect = (RectTransform)_hpFill.transform;
        _hpFillRect.SetParent(bg.rectTransform, false);
        _hpFillRect.anchorMin = Vector2.zero;
        _hpFillRect.anchorMax = Vector2.one;
        _hpFillRect.offsetMin = new Vector2(0.008f, 0.008f);
        _hpFillRect.offsetMax = new Vector2(-0.008f, -0.008f);

        // three amber pips (sips) on the left, three red pips (drunk) on the right
        _sipPips = MakePips(new Vector2(-0.44f, -0.20f), new Color(1f, 0.62f, 0.15f));
        _drunkPips = MakePips(new Vector2(0.44f, -0.20f), new Color(0.9f, 0.15f, 0.1f));
    }

    private Image[] MakePips(Vector2 pos, Color color)
    {
        var pips = new Image[3];
        for (int i = 0; i < 3; i++)
        {
            var pip = MakeImage(color);
            pip.rectTransform.sizeDelta = new Vector2(0.045f, 0.045f);
            pip.rectTransform.anchoredPosition = pos + new Vector2(0f, 0.052f * (i - 1));
            pips[i] = pip;
        }
        return pips;
    }

    private Text MakeText(Font font, int size, TextAnchor anchor)
    {
        var go = new GameObject("Text");
        go.transform.SetParent(transform, false);
        var text = go.AddComponent<Text>();
        text.font = font;
        text.fontSize = size;
        text.alignment = anchor;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        return text;
    }

    private Image MakeImage(Color color)
    {
        var go = new GameObject("Image");
        go.transform.SetParent(transform, false);
        var img = go.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    public void SetDead()
    {
        if (_group != null) _group.alpha = 0.35f;
        if (_name != null) _name.color = new Color(0.6f, 0.6f, 0.6f);
    }

    private void LateUpdate()
    {
        if (_owner == null) return;

        // billboard toward whichever camera is looking (cheap refresh, no
        // per-frame Camera.allCameras allocation)
        if (Time.unscaledTime >= _camRefreshAt)
        {
            _camRefreshAt = Time.unscaledTime + 0.5f;
            _camCache = Camera.allCamerasCount > 0 ? Camera.allCameras[0] : null;
        }
        if (_camCache != null)
        {
            var toCam = transform.position - _camCache.transform.position;
            if (toCam.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.LookRotation(toCam);
        }

        _name.text = _owner.FighterName.Value.ToString();

        float frac = Mathf.Clamp01(_owner.Hp.Value / _owner.MaxHp);
        _hpFillRect.anchorMax = new Vector2(Mathf.Max(frac, 0.001f), 1f);
        _hpFill.color = Color.Lerp(new Color(0.85f, 0.2f, 0.15f), new Color(0.25f, 0.8f, 0.25f), frac);

        for (int i = 0; i < 3; i++)
        {
            _sipPips[i].color = i < _owner.SipsLeft.Value
                ? new Color(1f, 0.62f, 0.15f) : new Color(1f, 1f, 1f, 0.12f);
            _drunkPips[i].color = i < _owner.DrunkLevel.Value
                ? new Color(0.9f, 0.15f, 0.1f) : new Color(1f, 1f, 1f, 0.12f);
        }
    }
}

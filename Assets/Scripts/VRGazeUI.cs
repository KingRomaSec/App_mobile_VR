using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Bouton d'interface VR activable au regard (viseur) + appui sur le bouton du casque,
/// ou en le fixant (clic automatique après quelques secondes).
/// </summary>
public class GazeButton
{
    public RectTransform Rect;
    public Image Background;
    public Text Label;
    /// <summary>Appelé avec la position du clic dans le bouton (0..1, 0..1), utile pour la barre de progression.</summary>
    public Action<Vector2> OnClick;
    public bool AllowDwell = true;
    public Color Normal = new Color(0.16f, 0.18f, 0.24f, 0.95f);
    public Color Hover = new Color(0.10f, 0.45f, 0.85f, 1f);

    public bool IsActive => Rect != null && Rect.gameObject.activeInHierarchy;

    public void SetLabel(string text)
    {
        if (Label != null) Label.text = text;
    }
}

/// <summary>
/// Fabrique d'éléments uGUI en espace monde (coordonnées en "pixels", origine en haut à gauche).
/// </summary>
public static class VRUI
{
    private static Font _font;
    private static Sprite _circle;
    private static Sprite _ring;

    public static Font Font
    {
        get
        {
            if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return _font;
        }
    }

    public static Canvas CreateCanvas(string name, Vector2 size, float metersPerPixel, int sortingOrder = 0)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Canvas));
        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = sortingOrder;
        var rt = (RectTransform)go.transform;
        rt.sizeDelta = size;
        rt.localScale = Vector3.one * metersPerPixel;
        return canvas;
    }

    public static RectTransform CreateRect(string name, Transform parent, float x, float y, float w, float h)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(x, -y);
        rt.sizeDelta = new Vector2(w, h);
        return rt;
    }

    /// <summary>Étire un élément pour remplir entièrement son parent.</summary>
    public static RectTransform Fill(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        return rt;
    }

    public static Image CreateImage(string name, Transform parent, float x, float y, float w, float h, Color color)
    {
        var rt = CreateRect(name, parent, x, y, w, h);
        var img = rt.gameObject.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    public static Text CreateText(string name, Transform parent, float x, float y, float w, float h,
                                  string text, int fontSize, TextAnchor align, Color color)
    {
        var rt = CreateRect(name, parent, x, y, w, h);
        var t = rt.gameObject.AddComponent<Text>();
        t.font = Font;
        t.text = text;
        t.fontSize = fontSize;
        t.alignment = align;
        t.color = color;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Truncate;
        t.raycastTarget = false;
        return t;
    }

    public static GazeButton CreateButton(Transform parent, float x, float y, float w, float h,
                                          string label, int fontSize, Action onClick)
    {
        var btn = new GazeButton();
        btn.Background = CreateImage("Btn_" + label, parent, x, y, w, h, btn.Normal);
        btn.Rect = btn.Background.rectTransform;
        btn.Label = CreateText("Label", btn.Rect, 0, 0, w, h, label, fontSize, TextAnchor.MiddleCenter, Color.white);
        Fill(btn.Label.rectTransform);
        btn.OnClick = _ => onClick?.Invoke();
        return btn;
    }

    /// <summary>Disque blanc (pour le viseur).</summary>
    public static Sprite Circle => _circle != null ? _circle : (_circle = MakeCircleSprite(128, 0f));

    /// <summary>Anneau blanc (pour la progression du clic au regard).</summary>
    public static Sprite Ring => _ring != null ? _ring : (_ring = MakeCircleSprite(128, 0.72f));

    private static Sprite MakeCircleSprite(int size, float innerRatio)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        float r = size * 0.5f;
        float inner = r * innerRatio;
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r));
                float a = Mathf.Clamp01(r - d) * (innerRatio > 0f ? Mathf.Clamp01(d - inner) : 1f);
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
    }
}

/// <summary>
/// Gère le regard : bouton visé, surbrillance, viseur et clic automatique après fixation.
/// </summary>
public class VRGazeController
{
    public float DwellTime = 2f;
    public bool DwellEnabled = true;

    public GazeButton Hovered { get; private set; }

    private readonly List<GazeButton> _buttons = new List<GazeButton>();
    private readonly Camera _camera;
    private readonly Transform _reticleRoot;
    private readonly Image _reticleFill;
    private float _hoverStart;
    private bool _dwellFired;
    private Vector2 _hoverLocal;

    public VRGazeController(Camera camera)
    {
        _camera = camera;

        // Viseur : petit point blanc + anneau de progression, placé à la distance de l'objet visé
        var canvas = VRUI.CreateCanvas("Reticle", new Vector2(100, 100), 0.0004f, 100);
        _reticleRoot = canvas.transform;
        _reticleRoot.SetParent(camera.transform, false);
        var dot = VRUI.CreateImage("Dot", _reticleRoot, 38, 38, 24, 24, Color.white);
        dot.sprite = VRUI.Circle;
        _reticleFill = VRUI.CreateImage("Progress", _reticleRoot, 0, 0, 100, 100, new Color(0.3f, 0.8f, 1f, 1f));
        _reticleFill.sprite = VRUI.Ring;
        _reticleFill.type = Image.Type.Filled;
        _reticleFill.fillMethod = Image.FillMethod.Radial360;
        _reticleFill.fillOrigin = (int)Image.Origin360.Top;
        _reticleFill.fillAmount = 0f;
        SetReticleDistance(3f);
    }

    public void Register(GazeButton button) => _buttons.Add(button);

    public void SetReticleVisible(bool visible) => _reticleRoot.gameObject.SetActive(visible);

    public void Update()
    {
        var ray = new Ray(_camera.transform.position, _camera.transform.forward);
        GazeButton hit = null;
        float hitDistance = 3f;
        Vector2 local = Vector2.zero;

        foreach (var b in _buttons)
        {
            if (!b.IsActive) continue;
            var plane = new Plane(b.Rect.forward, b.Rect.position);
            if (!plane.Raycast(ray, out float enter) || enter <= 0f) continue;

            Vector3 p = b.Rect.InverseTransformPoint(ray.GetPoint(enter));
            Rect r = b.Rect.rect;
            if (r.Contains(new Vector2(p.x, p.y)) && (hit == null || enter < hitDistance))
            {
                hit = b;
                hitDistance = enter;
                local = new Vector2((p.x - r.xMin) / r.width, (p.y - r.yMin) / r.height);
            }
        }

        if (hit != Hovered)
        {
            if (Hovered != null && Hovered.Background != null) Hovered.Background.color = Hovered.Normal;
            Hovered = hit;
            _hoverStart = Time.unscaledTime;
            _dwellFired = false;
            if (Hovered != null && Hovered.Background != null) Hovered.Background.color = Hovered.Hover;
        }
        _hoverLocal = local;

        // Clic automatique après fixation
        float progress = 0f;
        if (Hovered != null && DwellEnabled && Hovered.AllowDwell && !_dwellFired)
        {
            progress = Mathf.Clamp01((Time.unscaledTime - _hoverStart) / DwellTime);
            if (progress >= 1f)
            {
                _dwellFired = true;
                progress = 0f;
                Click();
            }
        }
        _reticleFill.fillAmount = progress;

        SetReticleDistance(Hovered != null ? hitDistance - 0.02f : 3f);
    }

    /// <summary>Active le bouton visé. Retourne false si aucun bouton n'est visé.</summary>
    public bool Click()
    {
        if (Hovered == null) return false;
        var target = Hovered;
        _hoverStart = Time.unscaledTime;
        _dwellFired = true; // évite un 2e clic automatique tant que le regard ne quitte pas le bouton
        target.OnClick?.Invoke(_hoverLocal);
        return true;
    }

    private void SetReticleDistance(float distance)
    {
        // Taille angulaire constante quelle que soit la distance
        _reticleRoot.localPosition = new Vector3(0f, 0f, distance);
        _reticleRoot.localRotation = Quaternion.identity;
        _reticleRoot.localScale = Vector3.one * (0.00022f * distance);
    }
}

using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

// The little bit of screen UI the business card keeps: the character's speech bubble, the hint banner, the sound
// button and the lost-card overlay. Everything else is 3D (BusinessCardController.Stages.cs).
// Style: the card's own navy, orange and cream.
public partial class BusinessCardController
{
    static readonly Color Navy = new Color32(0x14, 0x21, 0x3D, 0xFF);
    static readonly Color NavyGlass = new Color32(0x14, 0x21, 0x3D, 0xEB);
    static readonly Color NavyLight = new Color32(0x24, 0x38, 0x66, 0xFF);
    static readonly Color Orange = new Color32(0xF2, 0x6A, 0x1B, 0xFF);
    static readonly Color Cream = new Color32(0xF7, 0xF5, 0xF0, 0xFF);
    static readonly Color Slate = new Color32(0xAE, 0xB7, 0xCC, 0xFF);

    class Ui
    {
        public GameObject root, lost, banner, bubble;
        public Text bubbleText;
        public Image soundIcon;
        public GameObject[] tags;
        public Coroutine bubbleRoutine, hideRoutine;
    }

    readonly Ui ui = new Ui();

    void BuildUI()
    {
        var canvasGo = new GameObject("Business UI", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(CanvasOrientation));
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.matchWidthOrHeight = 0.5f;
        ui.root = canvasGo;
        var root = (RectTransform)canvasGo.transform;
        if (!font) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        // the mini-me's speech bubble
        ui.bubble = MakePill(root, "", Cream, Navy, 38, false, out ui.bubbleText);
        ui.bubble.AddComponent<UIPopIn>();
        Follow(ui.bubble, avatarHead, new Vector2(0, 60));
        ui.bubble.SetActive(false);

        // the hint at the top
        ui.banner = MakePill(root, "Tap a node to explore", NavyGlass, Cream, 38, true, out _);
        var bannerRect = (RectTransform)ui.banner.transform;
        bannerRect.anchorMin = bannerRect.anchorMax = new Vector2(0.5f, 1);
        bannerRect.anchoredPosition = new Vector2(0, -160);
        ui.banner.AddComponent<UISlideIn>().from = new Vector2(0, 300);
        ui.banner.SetActive(false);

        // sound on/off, top right
        var sound = Box("Sound Button", root, NavyGlass);
        Place(sound.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-30, -30), new Vector2(110, 110), new Vector2(1, 1));
        ui.soundIcon = Icon(sound.transform, iconMusic, Cream, 60);
        MakeButton(sound.gameObject, sound, ToggleSound);

        // back to the home screen, top left
        var home = Box("Home Button", root, NavyGlass);
        Place(home.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(30, -30), new Vector2(110, 110), new Vector2(0, 1));
        Icon(home.transform, iconPrev, Cream, 60);
        MakeButton(home.gameObject, home, () => { Haptics.Buzz(15, Haptics.Light); FindAnyObjectByType<CardHub>().ReturnHome(); });

        // "Find the card again"
        var dim = Box("Lost", root, new Color(0.04f, 0.08f, 0.15f, 0.6f));
        Stretch(dim.rectTransform, 0, 0, 0, 0);
        ui.lost = dim.gameObject;
        var card = Box("Card", dim.transform, NavyGlass);
        Place(card.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(860, 300));
        card.gameObject.AddComponent<UIPopIn>();
        var diamond = Icon(card.transform, null, Orange, 34);
        Place(diamond.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -50), new Vector2(34, 34));
        diamond.rectTransform.localEulerAngles = new Vector3(0, 0, 45);
        Place(Label(card.transform, "Find the card again", 58, Cream, FontStyle.Bold, TextAnchor.MiddleCenter).rectTransform,
              new Vector2(0, 0.5f), new Vector2(1, 0.5f), new Vector2(0, 10), new Vector2(0, 90));
        Place(Label(card.transform, "Point your camera back at your business card", 34, Slate, FontStyle.Normal, TextAnchor.MiddleCenter).rectTransform,
              new Vector2(0, 0.5f), new Vector2(1, 0.5f), new Vector2(0, -70), new Vector2(-60, 70));
        ui.lost.SetActive(false);

        ui.root.SetActive(false); // CardHub shows it when the card is found
    }

    void ResetUi()
    {
        ui.lost.SetActive(false);
        ui.banner.SetActive(false);
        ui.bubble.SetActive(false);
        if (ui.tags != null) foreach (var tag in ui.tags) tag.SetActive(false);
        openStage = -1;
        if (stageRoot != null) foreach (var root in stageRoot) root.gameObject.SetActive(false);
    }

    // ---------- Banner, labels, bubble ----------

    void ShowBanner(bool on) => ui.banner.SetActive(on);
    void ShowTag(int i) => ui.tags[i].SetActive(true);

    void ShowBubble(string text, float seconds = 0f)
    {
        ui.bubbleText.text = text;
        if (ui.bubbleRoutine != null) StopCoroutine(ui.bubbleRoutine);
        ui.bubble.SetActive(false);
        ui.bubble.SetActive(true); // pops in again
        if (seconds > 0) ui.bubbleRoutine = StartCoroutine(HideBubbleAfter(seconds));
    }

    void HideBubble()
    {
        if (ui.bubbleRoutine != null) StopCoroutine(ui.bubbleRoutine);
        ui.bubbleRoutine = null;
        ui.bubble.SetActive(false);
    }

    IEnumerator HideBubbleAfter(float seconds)
    {
        yield return new WaitForSeconds(seconds);
        ui.bubble.SetActive(false);
        ui.bubbleRoutine = null;
    }

    // ---------- Building blocks ----------

    Image Box(string name, Transform parent, Color colour)
    {
        var image = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        image.transform.SetParent(parent, false);
        image.color = colour;
        if (panelSprite) { image.sprite = panelSprite; image.type = Image.Type.Sliced; }
        image.raycastTarget = false;
        return image;
    }

    Image Icon(Transform parent, Sprite sprite, Color colour, float size)
    {
        var image = new GameObject("Icon", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        image.transform.SetParent(parent, false);
        image.sprite = sprite;
        image.color = colour;
        image.raycastTarget = false;
        var rect = image.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(size, size);
        return image;
    }

    Text Label(Transform parent, string text, int size, Color colour, FontStyle style, TextAnchor anchor)
    {
        var label = new GameObject("Text", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
        label.transform.SetParent(parent, false);
        label.text = text;
        label.font = font;
        label.fontSize = size;
        label.fontStyle = style;
        label.color = colour;
        label.alignment = anchor;
        label.horizontalOverflow = HorizontalWrapMode.Wrap;
        label.verticalOverflow = VerticalWrapMode.Overflow;
        label.raycastTarget = false;
        return label;
    }

    // A rounded label that sizes itself to its text, optionally with a small orange diamond in front.
    GameObject MakePill(Transform parent, string text, Color fill, Color textColour, int size, bool diamond, out Text label)
    {
        var go = new GameObject("Pill", typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter), typeof(CanvasGroup));
        go.transform.SetParent(parent, false);
        var image = go.GetComponent<Image>();
        image.sprite = panelSprite;
        image.type = Image.Type.Sliced;
        image.color = fill;
        image.raycastTarget = false;
        var layout = go.GetComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(28, 28, 16, 16);
        layout.spacing = 16;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = layout.childForceExpandHeight = false;
        var fit = go.GetComponent<ContentSizeFitter>();
        fit.horizontalFit = fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        if (diamond)
        {
            var d = Icon(go.transform, null, Orange, 22);
            d.rectTransform.localEulerAngles = new Vector3(0, 0, 45);
            var element = d.gameObject.AddComponent<LayoutElement>();
            element.preferredWidth = element.preferredHeight = 22;
        }
        label = Label(go.transform, text, size, textColour, FontStyle.Bold, TextAnchor.MiddleCenter);
        label.horizontalOverflow = HorizontalWrapMode.Overflow;
        return go;
    }

    void Chip(Transform grid, string text, int size)
    {
        var chip = Box("Chip", grid, NavyLight);
        var label = Label(chip.transform, text, size, Cream, FontStyle.Bold, TextAnchor.MiddleCenter);
        Stretch(label.rectTransform, 12, 6, 12, 6);
        label.resizeTextForBestFit = true; // long names shrink to fit
        label.resizeTextMinSize = 20;
        label.resizeTextMaxSize = size;
    }

    void ContactButton(Transform parent, Sprite icon, string title, string value, UnityAction onClick)
    {
        var button = Box(title, parent, NavyLight);
        var element = button.gameObject.AddComponent<LayoutElement>();
        element.preferredHeight = 124;
        var disc = Box("Disc", button.transform, Orange);
        Place(disc.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(22, 0), new Vector2(84, 84), new Vector2(0, 0.5f));
        Icon(disc.transform, icon, Cream, 48);
        var name = Label(button.transform, title, 38, Cream, FontStyle.Bold, TextAnchor.LowerLeft);
        Place(name.rectTransform, new Vector2(0, 0.5f), new Vector2(1, 0.5f), new Vector2(128, 4), new Vector2(-150, 48), new Vector2(0, 0));
        var sub = Label(button.transform, value, 30, Slate, FontStyle.Normal, TextAnchor.UpperLeft);
        Place(sub.rectTransform, new Vector2(0, 0.5f), new Vector2(1, 0.5f), new Vector2(128, -4), new Vector2(-150, 44), new Vector2(0, 1));
        MakeButton(button.gameObject, button, onClick);
    }

    void ArrowButton(Transform parent, Sprite icon, int side, UnityAction onClick)
    {
        var button = Box("Arrow", parent, new Color32(0x14, 0x21, 0x3D, 0xCC));
        Place(button.rectTransform, new Vector2(side, 0.5f), new Vector2(side, 0.5f), new Vector2(side == 0 ? 14 : -14, 0), new Vector2(84, 84), new Vector2(side, 0.5f));
        Icon(button.transform, icon, Cream, 44);
        MakeButton(button.gameObject, button, onClick);
    }

    void MakeButton(GameObject go, Image graphic, UnityAction onClick)
    {
        graphic.raycastTarget = true;
        var button = go.AddComponent<Button>();
        button.targetGraphic = graphic;
        var colours = button.colors;
        colours.pressedColor = new Color(0.8f, 0.8f, 0.8f);
        button.colors = colours;
        button.onClick.AddListener(onClick);
        go.AddComponent<ButtonBounce>();
    }

    void Follow(GameObject go, Transform target, Vector2 offset)
    {
        var follow = go.AddComponent<UIFollow>();
        follow.target = target;
        follow.cam = arCamera;
        follow.offset = offset;
    }

    static void Vertical(GameObject go, float spacing)
    {
        var layout = go.AddComponent<VerticalLayoutGroup>();
        layout.spacing = spacing;
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
    }

    static Transform Grid(Transform parent, Vector2 cell, int columns, float spacing)
    {
        var go = new GameObject("Grid", typeof(RectTransform), typeof(GridLayoutGroup));
        go.transform.SetParent(parent, false);
        var grid = go.GetComponent<GridLayoutGroup>();
        grid.cellSize = cell;
        grid.spacing = new Vector2(spacing, spacing);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = columns;
        grid.childAlignment = TextAnchor.UpperLeft;
        return go.transform;
    }

    static void Place(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 position, Vector2 size, Vector2? pivot = null)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot ?? new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    // Fills the parent, inset by the given amounts.
    static void Stretch(RectTransform rect, float left, float bottom, float right, float top)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, -top);
    }
}

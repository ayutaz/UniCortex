using UnityEngine;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace UniCortex.Samples
{
    /// <summary>
    /// UGUI 要素を動的生成するユーティリティ。
    /// プレハブ不要で全 UI 要素をコードで構築する。
    /// </summary>
    public static class UIHelper
    {
        static readonly Color BgColor = new Color(0.15f, 0.15f, 0.2f, 1f);
        static readonly Color PanelColor = new Color(0.2f, 0.2f, 0.25f, 0.95f);
        static readonly Color ButtonColor = new Color(0.3f, 0.5f, 0.8f, 1f);
        static readonly Color InputBgColor = new Color(0.12f, 0.12f, 0.16f, 1f);
        static readonly Color ResultBgColor = new Color(0.18f, 0.18f, 0.22f, 1f);
        static readonly Color TextColor = Color.white;
        static readonly Color PlaceholderColor = new Color(0.6f, 0.6f, 0.6f, 1f);

        /// <summary>Canvas + EventSystem を生成する。</summary>
        public static Canvas CreateCanvas()
        {
            // Canvas
            var canvasGo = new GameObject("Canvas");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            // Background
            var bgGo = new GameObject("Background");
            bgGo.transform.SetParent(canvasGo.transform, false);
            var bgImg = bgGo.AddComponent<Image>();
            bgImg.color = BgColor;
            var bgRect = bgGo.GetComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;

            // EventSystem
            if (Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
            {
                var esGo = new GameObject("EventSystem");
                esGo.AddComponent<UnityEngine.EventSystems.EventSystem>();
                esGo.AddComponent<InputSystemUIInputModule>();
            }

            return canvas;
        }

        /// <summary>タイトルラベルを生成する。</summary>
        public static Text CreateTitle(Transform parent, string title)
        {
            var go = new GameObject("Title");
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<Text>();
            text.text = title;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 28;
            text.fontStyle = FontStyle.Bold;
            text.color = TextColor;
            text.alignment = TextAnchor.MiddleCenter;
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = new Vector2(1, 1);
            rect.pivot = new Vector2(0.5f, 1);
            rect.anchoredPosition = new Vector2(0, -10);
            rect.sizeDelta = new Vector2(0, 40);
            return text;
        }

        /// <summary>ラベルを生成する。</summary>
        public static Text CreateLabel(Transform parent, string labelText, int fontSize = 18)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<Text>();
            text.text = labelText;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.color = TextColor;
            text.alignment = TextAnchor.MiddleLeft;
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = 30;
            le.preferredHeight = 30;
            return text;
        }

        /// <summary>ボタンを生成する。</summary>
        public static Button CreateButton(Transform parent, string label, float height = 40)
        {
            var go = new GameObject("Button_" + label);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = ButtonColor;
            var btn = go.AddComponent<Button>();
            var colors = btn.colors;
            colors.highlightedColor = new Color(0.4f, 0.6f, 0.9f, 1f);
            colors.pressedColor = new Color(0.2f, 0.4f, 0.7f, 1f);
            btn.colors = colors;

            var textGo = new GameObject("Text");
            textGo.transform.SetParent(go.transform, false);
            var text = textGo.AddComponent<Text>();
            text.text = label;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 18;
            text.color = TextColor;
            text.alignment = TextAnchor.MiddleCenter;
            var textRect = textGo.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            var le = go.AddComponent<LayoutElement>();
            le.minHeight = height;
            le.preferredHeight = height;

            return btn;
        }

        /// <summary>テキスト入力フィールドを生成する。</summary>
        public static InputField CreateInputField(Transform parent, string placeholder = "", float height = 36)
        {
            var go = new GameObject("InputField");
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = InputBgColor;

            var textGo = new GameObject("Text");
            textGo.transform.SetParent(go.transform, false);
            var text = textGo.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 18;
            text.color = TextColor;
            text.supportRichText = false;
            var textRect = textGo.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(10, 2);
            textRect.offsetMax = new Vector2(-10, -2);

            var phGo = new GameObject("Placeholder");
            phGo.transform.SetParent(go.transform, false);
            var phText = phGo.AddComponent<Text>();
            phText.text = placeholder;
            phText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            phText.fontSize = 18;
            phText.fontStyle = FontStyle.Italic;
            phText.color = PlaceholderColor;
            var phRect = phGo.GetComponent<RectTransform>();
            phRect.anchorMin = Vector2.zero;
            phRect.anchorMax = Vector2.one;
            phRect.offsetMin = new Vector2(10, 2);
            phRect.offsetMax = new Vector2(-10, -2);

            var input = go.AddComponent<InputField>();
            input.textComponent = text;
            input.placeholder = phText;

            var le = go.AddComponent<LayoutElement>();
            le.minHeight = height;
            le.preferredHeight = height;

            return input;
        }

        /// <summary>ドロップダウンを生成する。</summary>
        public static Dropdown CreateDropdown(Transform parent, string[] options, float height = 36)
        {
            var go = new GameObject("Dropdown");
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = InputBgColor;

            // Label
            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(go.transform, false);
            var label = labelGo.AddComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = 18;
            label.color = TextColor;
            label.alignment = TextAnchor.MiddleLeft;
            var labelRect = labelGo.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(10, 0);
            labelRect.offsetMax = new Vector2(-30, 0);

            // Arrow
            var arrowGo = new GameObject("Arrow");
            arrowGo.transform.SetParent(go.transform, false);
            var arrowImg = arrowGo.AddComponent<Image>();
            arrowImg.color = TextColor;
            var arrowRect = arrowGo.GetComponent<RectTransform>();
            arrowRect.anchorMin = new Vector2(1, 0.5f);
            arrowRect.anchorMax = new Vector2(1, 0.5f);
            arrowRect.pivot = new Vector2(1, 0.5f);
            arrowRect.anchoredPosition = new Vector2(-10, 0);
            arrowRect.sizeDelta = new Vector2(12, 12);

            // Template
            var templateGo = new GameObject("Template");
            templateGo.transform.SetParent(go.transform, false);
            var templateImg = templateGo.AddComponent<Image>();
            templateImg.color = new Color(0.15f, 0.15f, 0.2f, 1f);
            var templateRect = templateGo.GetComponent<RectTransform>();
            templateRect.anchorMin = new Vector2(0, 0);
            templateRect.anchorMax = new Vector2(1, 0);
            templateRect.pivot = new Vector2(0.5f, 1);
            templateRect.anchoredPosition = Vector2.zero;
            templateRect.sizeDelta = new Vector2(0, 150);
            var scrollRect = templateGo.AddComponent<ScrollRect>();

            // Viewport
            var viewportGo = new GameObject("Viewport");
            viewportGo.transform.SetParent(templateGo.transform, false);
            var viewportRect = viewportGo.AddComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = Vector2.zero;
            viewportRect.offsetMax = Vector2.zero;
            viewportGo.AddComponent<Mask>().showMaskGraphic = false;
            viewportGo.AddComponent<Image>();

            // Content
            var contentGo = new GameObject("Content");
            contentGo.transform.SetParent(viewportGo.transform, false);
            var contentRect = contentGo.AddComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0, 1);
            contentRect.anchorMax = new Vector2(1, 1);
            contentRect.pivot = new Vector2(0.5f, 1);
            contentRect.sizeDelta = new Vector2(0, 28);

            // Item
            var itemGo = new GameObject("Item");
            itemGo.transform.SetParent(contentGo.transform, false);
            var itemToggle = itemGo.AddComponent<Toggle>();
            var itemRect = itemGo.GetComponent<RectTransform>();
            itemRect.anchorMin = new Vector2(0, 0.5f);
            itemRect.anchorMax = new Vector2(1, 0.5f);
            itemRect.sizeDelta = new Vector2(0, 28);

            // Item Background
            var itemBgGo = new GameObject("Item Background");
            itemBgGo.transform.SetParent(itemGo.transform, false);
            var itemBgImg = itemBgGo.AddComponent<Image>();
            itemBgImg.color = new Color(0.25f, 0.25f, 0.3f, 1f);
            var itemBgRect = itemBgGo.GetComponent<RectTransform>();
            itemBgRect.anchorMin = Vector2.zero;
            itemBgRect.anchorMax = Vector2.one;
            itemBgRect.offsetMin = Vector2.zero;
            itemBgRect.offsetMax = Vector2.zero;

            // Item Checkmark
            var checkGo = new GameObject("Item Checkmark");
            checkGo.transform.SetParent(itemBgGo.transform, false);
            var checkImg = checkGo.AddComponent<Image>();
            checkImg.color = ButtonColor;
            var checkRect = checkGo.GetComponent<RectTransform>();
            checkRect.anchorMin = Vector2.zero;
            checkRect.anchorMax = Vector2.one;
            checkRect.offsetMin = Vector2.zero;
            checkRect.offsetMax = Vector2.zero;

            itemToggle.targetGraphic = itemBgImg;
            itemToggle.graphic = checkImg;

            // Item Label
            var itemLabelGo = new GameObject("Item Label");
            itemLabelGo.transform.SetParent(itemGo.transform, false);
            var itemLabel = itemLabelGo.AddComponent<Text>();
            itemLabel.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            itemLabel.fontSize = 16;
            itemLabel.color = TextColor;
            itemLabel.alignment = TextAnchor.MiddleLeft;
            var itemLabelRect = itemLabelGo.GetComponent<RectTransform>();
            itemLabelRect.anchorMin = Vector2.zero;
            itemLabelRect.anchorMax = Vector2.one;
            itemLabelRect.offsetMin = new Vector2(10, 0);
            itemLabelRect.offsetMax = new Vector2(-10, 0);

            scrollRect.viewport = viewportRect;
            scrollRect.content = contentRect;

            templateGo.SetActive(false);

            var dropdown = go.AddComponent<Dropdown>();
            dropdown.captionText = label;
            dropdown.itemText = itemLabel;
            dropdown.template = templateRect;

            dropdown.ClearOptions();
            var optionList = new System.Collections.Generic.List<Dropdown.OptionData>();
            foreach (var opt in options)
                optionList.Add(new Dropdown.OptionData(opt));
            dropdown.AddOptions(optionList);

            var le = go.AddComponent<LayoutElement>();
            le.minHeight = height;
            le.preferredHeight = height;

            return dropdown;
        }

        /// <summary>スライダーを生成する。</summary>
        public static Slider CreateSlider(Transform parent, float min, float max, float defaultValue, bool wholeNumbers = false, float height = 30)
        {
            var go = new GameObject("Slider");
            go.transform.SetParent(parent, false);

            // Background
            var bgGo = new GameObject("Background");
            bgGo.transform.SetParent(go.transform, false);
            var bgImg = bgGo.AddComponent<Image>();
            bgImg.color = new Color(0.1f, 0.1f, 0.14f, 1f);
            var bgRect = bgGo.GetComponent<RectTransform>();
            bgRect.anchorMin = new Vector2(0, 0.25f);
            bgRect.anchorMax = new Vector2(1, 0.75f);
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;

            // Fill Area
            var fillAreaGo = new GameObject("Fill Area");
            fillAreaGo.transform.SetParent(go.transform, false);
            var fillAreaRect = fillAreaGo.AddComponent<RectTransform>();
            fillAreaRect.anchorMin = new Vector2(0, 0.25f);
            fillAreaRect.anchorMax = new Vector2(1, 0.75f);
            fillAreaRect.offsetMin = Vector2.zero;
            fillAreaRect.offsetMax = Vector2.zero;

            var fillGo = new GameObject("Fill");
            fillGo.transform.SetParent(fillAreaGo.transform, false);
            var fillImg = fillGo.AddComponent<Image>();
            fillImg.color = ButtonColor;
            var fillRect = fillGo.GetComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;

            // Handle Slide Area
            var handleAreaGo = new GameObject("Handle Slide Area");
            handleAreaGo.transform.SetParent(go.transform, false);
            var handleAreaRect = handleAreaGo.AddComponent<RectTransform>();
            handleAreaRect.anchorMin = Vector2.zero;
            handleAreaRect.anchorMax = Vector2.one;
            handleAreaRect.offsetMin = Vector2.zero;
            handleAreaRect.offsetMax = Vector2.zero;

            var handleGo = new GameObject("Handle");
            handleGo.transform.SetParent(handleAreaGo.transform, false);
            var handleImg = handleGo.AddComponent<Image>();
            handleImg.color = Color.white;
            var handleRect = handleGo.GetComponent<RectTransform>();
            handleRect.sizeDelta = new Vector2(16, 0);
            handleRect.anchorMin = new Vector2(0, 0);
            handleRect.anchorMax = new Vector2(0, 1);

            var slider = go.AddComponent<Slider>();
            slider.fillRect = fillRect;
            slider.handleRect = handleRect;
            slider.minValue = min;
            slider.maxValue = max;
            slider.wholeNumbers = wholeNumbers;
            slider.value = defaultValue;

            var le = go.AddComponent<LayoutElement>();
            le.minHeight = height;
            le.preferredHeight = height;

            return slider;
        }

        /// <summary>トグルを生成する。</summary>
        public static Toggle CreateToggle(Transform parent, string label, bool defaultValue = true, float height = 30)
        {
            var go = new GameObject("Toggle_" + label);
            go.transform.SetParent(parent, false);

            var bgGo = new GameObject("Background");
            bgGo.transform.SetParent(go.transform, false);
            var bgImg = bgGo.AddComponent<Image>();
            bgImg.color = InputBgColor;
            var bgRect = bgGo.GetComponent<RectTransform>();
            bgRect.anchorMin = new Vector2(0, 0.5f);
            bgRect.anchorMax = new Vector2(0, 0.5f);
            bgRect.pivot = new Vector2(0, 0.5f);
            bgRect.anchoredPosition = new Vector2(0, 0);
            bgRect.sizeDelta = new Vector2(24, 24);

            var checkGo = new GameObject("Checkmark");
            checkGo.transform.SetParent(bgGo.transform, false);
            var checkImg = checkGo.AddComponent<Image>();
            checkImg.color = ButtonColor;
            var checkRect = checkGo.GetComponent<RectTransform>();
            checkRect.anchorMin = new Vector2(0.1f, 0.1f);
            checkRect.anchorMax = new Vector2(0.9f, 0.9f);
            checkRect.offsetMin = Vector2.zero;
            checkRect.offsetMax = Vector2.zero;

            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(go.transform, false);
            var text = labelGo.AddComponent<Text>();
            text.text = label;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 16;
            text.color = TextColor;
            text.alignment = TextAnchor.MiddleLeft;
            var labelRect = labelGo.GetComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0, 0);
            labelRect.anchorMax = new Vector2(1, 1);
            labelRect.offsetMin = new Vector2(30, 0);
            labelRect.offsetMax = Vector2.zero;

            var toggle = go.AddComponent<Toggle>();
            toggle.targetGraphic = bgImg;
            toggle.graphic = checkImg;
            toggle.isOn = defaultValue;

            var le = go.AddComponent<LayoutElement>();
            le.minHeight = height;
            le.preferredHeight = height;

            return toggle;
        }

        /// <summary>スクロール可能な結果表示パネルを生成する。テキストコンポーネントを返す。</summary>
        public static Text CreateScrollableResult(Transform parent, float minHeight = 300)
        {
            var go = new GameObject("ResultPanel");
            go.transform.SetParent(parent, false);
            var panelImg = go.AddComponent<Image>();
            panelImg.color = ResultBgColor;

            var scrollGo = new GameObject("Scroll View");
            scrollGo.transform.SetParent(go.transform, false);
            var scrollRect = scrollGo.AddComponent<ScrollRect>();
            var scrollRectTr = scrollGo.GetComponent<RectTransform>();
            scrollRectTr.anchorMin = Vector2.zero;
            scrollRectTr.anchorMax = Vector2.one;
            scrollRectTr.offsetMin = new Vector2(5, 5);
            scrollRectTr.offsetMax = new Vector2(-5, -5);

            // Viewport
            var viewportGo = new GameObject("Viewport");
            viewportGo.transform.SetParent(scrollGo.transform, false);
            viewportGo.AddComponent<Image>();
            viewportGo.AddComponent<Mask>().showMaskGraphic = false;
            var viewportRect = viewportGo.GetComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = Vector2.zero;
            viewportRect.offsetMax = Vector2.zero;

            // Content
            var contentGo = new GameObject("Content");
            contentGo.transform.SetParent(viewportGo.transform, false);
            var contentRect = contentGo.AddComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0, 1);
            contentRect.anchorMax = new Vector2(1, 1);
            contentRect.pivot = new Vector2(0, 1);
            contentRect.sizeDelta = new Vector2(0, 0);
            var csf = contentGo.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Text
            var textGo = new GameObject("ResultText");
            textGo.transform.SetParent(contentGo.transform, false);
            var text = textGo.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 16;
            text.color = TextColor;
            text.alignment = TextAnchor.UpperLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            var textRect = textGo.GetComponent<RectTransform>();
            textRect.anchorMin = new Vector2(0, 1);
            textRect.anchorMax = new Vector2(1, 1);
            textRect.pivot = new Vector2(0, 1);
            textRect.anchoredPosition = Vector2.zero;
            textRect.sizeDelta = new Vector2(0, 0);
            var textCsf = textGo.AddComponent<ContentSizeFitter>();
            textCsf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var layoutEl = textGo.AddComponent<LayoutElement>();
            layoutEl.flexibleWidth = 1;

            scrollRect.viewport = viewportRect;
            scrollRect.content = contentRect;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;

            var le = go.AddComponent<LayoutElement>();
            le.minHeight = minHeight;
            le.flexibleHeight = 1;

            return text;
        }

        /// <summary>ステータスバーを生成する。</summary>
        public static Text CreateStatusBar(Transform parent)
        {
            var go = new GameObject("StatusBar");
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = new Color(0.1f, 0.1f, 0.14f, 1f);
            var text = CreateLabel(go.transform, "Ready", 14);
            text.color = new Color(0.8f, 0.9f, 0.8f, 1f);
            text.alignment = TextAnchor.MiddleLeft;
            var textRect = text.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(10, 0);
            textRect.offsetMax = new Vector2(-10, 0);
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = 30;
            le.preferredHeight = 30;
            return text;
        }

        /// <summary>水平区切り線を生成する。</summary>
        public static void CreateSeparator(Transform parent, float height = 2)
        {
            var go = new GameObject("Separator");
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = new Color(0.4f, 0.4f, 0.5f, 0.5f);
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = height;
            le.preferredHeight = height;
        }

        /// <summary>Vertical Layout Group 付きパネルを生成する。</summary>
        public static RectTransform CreateVerticalPanel(Transform parent, float padding = 10, float spacing = 6)
        {
            var go = new GameObject("Panel");
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = PanelColor;
            var vlg = go.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset((int)padding, (int)padding, (int)padding, (int)padding);
            vlg.spacing = spacing;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            return go.GetComponent<RectTransform>();
        }

        /// <summary>Horizontal Layout Group 付きパネルを生成する。</summary>
        public static RectTransform CreateHorizontalPanel(Transform parent, float spacing = 6, float height = 36)
        {
            var go = new GameObject("HPanel");
            go.transform.SetParent(parent, false);
            var hlg = go.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = spacing;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = height;
            le.preferredHeight = height;
            return go.GetComponent<RectTransform>();
        }

        /// <summary>スライダー + 値ラベルのペアを生成する。</summary>
        public static (Slider slider, Text valueLabel) CreateSliderWithLabel(Transform parent, string label, float min, float max, float defaultValue, bool wholeNumbers = false, string format = "F1")
        {
            var row = CreateHorizontalPanel(parent, 6, 30);

            var lbl = CreateLabel(row, label, 16);
            var lblLe = lbl.GetComponent<LayoutElement>();
            lblLe.preferredWidth = 120;
            lblLe.flexibleWidth = 0;

            var slider = CreateSlider(row, min, max, defaultValue, wholeNumbers);
            var sliderLe = slider.GetComponent<LayoutElement>();
            sliderLe.flexibleWidth = 1;

            var valLabel = CreateLabel(row, FormatSliderValue(defaultValue, wholeNumbers, format), 16);
            valLabel.alignment = TextAnchor.MiddleRight;
            var valLe = valLabel.GetComponent<LayoutElement>();
            valLe.preferredWidth = 60;
            valLe.flexibleWidth = 0;

            string fmt = format;
            bool whole = wholeNumbers;
            slider.onValueChanged.AddListener(v => { valLabel.text = FormatSliderValue(v, whole, fmt); });

            return (slider, valLabel);
        }

        static string FormatSliderValue(float value, bool wholeNumbers, string format)
        {
            return wholeNumbers ? ((int)value).ToString() : value.ToString(format);
        }
    }
}

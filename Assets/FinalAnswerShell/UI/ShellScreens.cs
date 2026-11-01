using System;
using UnityEngine;
using UnityEngine.UI;

namespace FinalAnswer.UI
{
    /// <summary>Thin always-on strip with the orthogonal connection state.</summary>
    public sealed class StatusBar
    {
        readonly Text text;
        readonly Button retry;
        readonly RectTransform root;

        public StatusBar(Transform parent, Action onRetry)
        {
            var rect = root = UiKit.Rect("StatusBar", parent);
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = new Vector2(1, 1);
            rect.pivot = new Vector2(0.5f, 1);
            rect.sizeDelta = new Vector2(0, 48);
            rect.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0.6f);
            text = UiKit.Label(rect, "", 24, Theme.Muted, TextAnchor.MiddleLeft);
            UiKit.Stretch(text.rectTransform, 12);
            retry = UiKit.Button(rect, "ПОВТОРИТЬ", onRetry, height: 40, fontSize: 22);
            var r = (RectTransform)retry.transform;
            r.anchorMin = r.anchorMax = new Vector2(1, 0.5f);
            r.pivot = new Vector2(1, 0.5f);
            r.sizeDelta = new Vector2(220, 40);
            r.anchoredPosition = new Vector2(-8, 0);
            retry.gameObject.SetActive(false);
        }

        public void Set(string message, Color color, bool canRetry)
        {
            text.text = message;
            text.color = color;
            retry.gameObject.SetActive(canRetry);
            // Only shown when there is something to say (connection trouble, closed room).
            root.gameObject.SetActive(!string.IsNullOrEmpty(message));
            root.SetAsLastSibling();
        }
    }
}

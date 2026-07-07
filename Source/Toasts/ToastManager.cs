using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using UnityEngine;
using UnityEngine.UI;

namespace MapWarp.Source.Toasts;

// Minimal on-screen toast overlay, vendored from DevUtils (trimmed to the plain-text path MapWarp uses) so the
// mod carries no hard dependency on DevUtils. Replace with the Silksong modding API's notifications once one
// exists upstream. Self-hosting: Install() creates the overlay GameObject, the static Toast() queues messages,
// the component pumps its own Update. The plugin destroys the GameObject on unload (hot reload).
public class ToastManager : MonoBehaviour {
    private const float MaxToastAge = 5;

    private static ToastManager? instance;

    private readonly List<ToastMessage> toasts = [];
    private RectTransform canvasRt = null!;
    private Text toastText = null!;
    private float lastKnownCanvasHeight;
    private int maxToastCount;
    private bool toastsDirty;

    private record struct ToastMessage(float StartTime, string Text);

    private static float Now => Time.time;

    // Create the overlay canvas + text. Idempotent. Call from the plugin's Awake.
    public static void Install() {
        if (instance != null) return;
        var go = new GameObject("MapWarpToasts");
        DontDestroyOnLoad(go);
        go.AddComponent<ToastManager>();
    }

    [PublicAPI]
    public static void Toast(object? message) {
        var text = message?.ToString() ?? "null";
        Log.Info($"Toast: {text}");
        instance?.AddToastMessage(text);
    }

    private void Awake() {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        gameObject.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        canvasRt = canvas.GetComponent<RectTransform>();

        var toastTextObj = new GameObject("Toast");
        toastTextObj.transform.SetParent(canvasRt);
        toastText = toastTextObj.AddComponent<Text>();
        toastText.alignment = TextAnchor.LowerRight;
        toastText.fontSize = 8;
        toastText.color = Color.white;
        toastText.text = "";
        toastText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");

        var rt = toastText.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(0, 10);
        rt.offsetMax = new Vector2(-10, 0);

        instance = this;
    }

    private void OnDestroy() {
        if (instance == this) instance = null;
    }

    private void Update() {
        try {
            // Idle fast path: nothing to age out or redraw. Skips the closure allocation RemoveAll would incur
            // every frame otherwise (the predicate captures `now`).
            if (toasts.Count == 0 && !toastsDirty) return;

            var now = Now;
            toastsDirty |= toasts.RemoveAll(toast => now - toast.StartTime > MaxToastAge) > 0;
            RefreshMaxToastCount();
            if (toastsDirty) {
                toastText.text = string.Join('\n', toasts.Select(toast => toast.Text));
                toastsDirty = false;
            }
        } catch (Exception e) {
            Log.Error(e);
        }
    }

    private void AddToastMessage(string message) {
        RefreshMaxToastCount();
        if (maxToastCount == 0) maxToastCount = 1;
        if (toasts.Count >= maxToastCount) toasts.RemoveAt(0);
        toasts.Add(new ToastMessage(Now, message));
        toastsDirty = true;
    }

    private int ComputeMaxToastCount() {
        var font = toastText.font;
        var lineHeight = font.lineHeight * ((float)toastText.fontSize / font.fontSize) * toastText.lineSpacing;
        return Mathf.Max(1, Mathf.FloorToInt(canvasRt.rect.height * 0.9f / lineHeight));
    }

    private void RefreshMaxToastCount() {
        var h = canvasRt.rect.height;
        if (h == lastKnownCanvasHeight) return;
        lastKnownCanvasHeight = h;
        maxToastCount = ComputeMaxToastCount();
    }
}

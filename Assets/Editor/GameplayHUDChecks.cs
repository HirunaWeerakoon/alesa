using System;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Isolated UI API checks; does not change the real player's health or inventory.</summary>
public static class GameplayHUDChecks
{
    [MenuItem("Alesa/Tests/Check Gameplay HUD (Play Mode)")]
    public static void Run()
    {
        if (!Application.isPlaying) { Debug.LogWarning("Enter Play mode to check the gameplay HUD."); return; }
        var host = new GameObject("Temporary Gameplay HUD API Checks");
        host.SetActive(false);
        try
        {
            var view = new GameplayHUDController.ViewReferences
            {
                root = new GameObject("Temporary Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup)).GetComponent<CanvasGroup>()
            };
            view.root.transform.SetParent(host.transform, false);
            view.actionLabel = Label(view.root.transform, "Action");
            view.detectionLabel = Label(view.root.transform, "Detection");
            view.weaponLabel = Label(view.root.transform, "Weapon");
            view.healthLabel = Label(view.root.transform, "Health");
            view.noiseTicks = new Image[4];
            view.healthSegments = new Image[10];
            for (int i = 0; i < view.noiseTicks.Length; i++) view.noiseTicks[i] = Tick(view.root.transform);
            for (int i = 0; i < view.healthSegments.Length; i++) view.healthSegments[i] = Tick(view.root.transform);
            var hud = host.AddComponent<GameplayHUDController>();
            hud.ConfigureView(view);
            host.SetActive(true);
            int noises = 0, aims = 0;
            hud.NoiseReported += (level, action) => noises++;
            hud.AimingStateChanged += aiming => aims++;
            hud.ReportNoise(1f, "GUNSHOT");
            hud.ReportNoise(0.1f, "WALKING");
            Check(hud.NoiseLevel == 1f && view.actionLabel.text == "GUNSHOT" && noises == 2,
                "A quieter report must not erase a gunshot peak");
            hud.ReportNoise(float.NaN, "INVALID");
            Check(hud.NoiseLevel == 1f && noises == 2, "Invalid noise must be ignored");
            hud.UpdateWeaponAmmo("Revolver", 5, 12);
            Check(view.weaponLabel.text == "Revolver  5 / 12", "Weapon text/payload mismatch");
            hud.UpdateHealthStatus(15f, 100f);
            Check(Mathf.Approximately(hud.HealthFraction, 0.15f) && view.healthLabel.text == "DANGER", "Critical health presentation");
            Check(view.healthSegments[0].color == view.healthSegments[1].color &&
                view.healthSegments[1].color != view.healthSegments[2].color, "15% health must light two of ten segments");
            hud.UpdateHealthStatus(50f, 100f);
            Check(view.healthLabel.text == "CAUTION", "Caution health presentation");
            hud.UpdateHealthStatus(float.NaN, 100f);
            hud.UpdateHealthStatus(100f, 0f);
            Check(hud.HealthFraction == 0.5f, "Invalid health must preserve the previous value");
            hud.SetAimingState(true); hud.SetAimingState(true); hud.SetAimingState(false);
            Check(aims == 2, "Aiming events must only fire on transitions");
            hud.SetDetectionStatus("SEARCHING");
            Check(view.detectionLabel.text == "SEARCHING", "AI detection override not applied");
            hud.SetDetectionStatus(null);
            Check(view.detectionLabel.text == "HEARD", "Clearing the override must restore the meter estimate");
            hud.SetVisible(false);
            Check(view.root.alpha == 0f && !view.root.blocksRaycasts, "Hidden HUD must not block UI input");
            Debug.Log("Gameplay HUD checks passed: peak preservation, API events, invalid inputs, ammo, health segments and labels, detection override, visibility.");
        }
        finally { host.SetActive(false); UnityEngine.Object.Destroy(host); }
    }

    private static TMP_Text Label(Transform parent, string name)
    {
        var item = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        item.transform.SetParent(parent, false);
        return item.GetComponent<TMP_Text>();
    }
    private static Image Tick(Transform parent)
    {
        var item = new GameObject("Tick", typeof(RectTransform), typeof(Image));
        item.transform.SetParent(parent, false);
        return item.GetComponent<Image>();
    }
    private static void Check(bool valid, string message) { if (!valid) throw new Exception(message); }
}

using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using BepInEx.Configuration;
using UnityEngine;
using BepInEx.Logging;

using UnityEngine.UI;
using UnityEngine.SceneManagement;

using System.Collections.Generic;
using System;

namespace SignalisFix
{
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    public class SignalisFix : BasePlugin
    {
        // Custom Resolution
        public static ConfigEntry<bool> bCustomResolution;
        public static ConfigEntry<float> fDesiredResolutionX;
        public static ConfigEntry<float> fDesiredResolutionY;
        public static ConfigEntry<int> iWindowMode;

        // Features
        public static ConfigEntry<bool> bUltrawideFixes;
        public static ConfigEntry<bool> bIntroSkip;

        internal static new ManualLogSource Log;

        public override void Load()
        {
            Log = base.Log;
            // Plugin startup logic
            Log.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");

            // Custom Resolution
            bCustomResolution = Config.Bind("Set Custom Resolution",
                                "CustomResolution",
                                 false, // Game resolution options should suffice.
                                "Set to true to enable the custom resolution below.");

            fDesiredResolutionX = Config.Bind("Set Custom Resolution",
                               "ResolutionWidth",
                               (float)Display.main.systemWidth, // Set default to display width so we don't leave an unsupported resolution as default.
                               "Set desired resolution width.");

            fDesiredResolutionY = Config.Bind("Set Custom Resolution",
                                "ResolutionHeight",
                                (float)Display.main.systemHeight, // Set default to display height so we don't leave an unsupported resolution as default.
                                "Set desired resolution height.");

            iWindowMode = Config.Bind("Set Custom Resolution",
                                "WindowMode",
                                (int)1,
                                new ConfigDescription("Set window mode. 1 = Fullscreen, 2 = Borderless, 3 = Windowed.",
                                new AcceptableValueRange<int>(1, 3)));

            // Features
            bUltrawideFixes = Config.Bind("General",
                                "UltrawideFixes",
                                true,
                                "Set to true to enable ultrawide fixes.");

            bIntroSkip = Config.Bind("General",
                                "IntroSkip",
                                 true,
                                "Skip intro logos.");

            if (bCustomResolution.Value)
            {
                var __0 = (int)fDesiredResolutionX.Value;
                var __1 = (int)fDesiredResolutionY.Value;
                var __3 = 0; // Default to highest refresh rate

                var fullscreenMode = iWindowMode.Value switch
                {
                    1 => FullScreenMode.ExclusiveFullScreen,
                    2 => FullScreenMode.FullScreenWindow,
                    3 => FullScreenMode.Windowed,
                    _ => FullScreenMode.ExclusiveFullScreen,
                };

                Screen.SetResolution(__0, __1, fullscreenMode, __3);
                Log.LogInfo($"Custom Resolution: Set resolution {__0}x{__1}@{__3}hz. Fullscreen = {fullscreenMode}.");
                Harmony.CreateAndPatchAll(typeof(CustomResolutionPatch));
            }

            if (bUltrawideFixes.Value)
            {
                Harmony.CreateAndPatchAll(typeof(UltrawidePatch));
            }

            if (bIntroSkip.Value)
            {
                Harmony.CreateAndPatchAll(typeof(IntroSkipPatch));
            }

        }


        [HarmonyPatch]
        [HarmonyPriority(1)]
        public class CustomResolutionPatch
        {
            // Apply custom resolution
            [HarmonyPatch(typeof(UnityEngine.Screen), nameof(UnityEngine.Screen.SetResolution), new Type[] { typeof(int), typeof(int), typeof(bool), typeof(int) })]
            [HarmonyPrefix]
            public static bool CustomRes(UnityEngine.Screen __instance, ref int __0, ref int __1, ref bool __2, ref int __3)
            {
                Log.LogInfo($"Skipped: Set resolution {__0}x{__1}@{__3}hz. Fullscreen = {__2}.");
                return false;
            }
        }

        [HarmonyPatch]
        [HarmonyPriority(2)]
        public class UltrawidePatch
        {
            // Aspect Ratio
            public static float DefaultAspectRatio = (float)16 / 9;
            public static float NewAspectRatio = (float)Screen.width / (float)Screen.height;
            public static float AspectMultiplier = NewAspectRatio / DefaultAspectRatio;

            public static float NewHorFloat = 640 * AspectMultiplier;
            public static int NewHor = (int)Mathf.CeilToInt(NewHorFloat);

            // Update scaling on res change. Allows it to be dynamic.
            [HarmonyPatch(typeof(Screen), nameof(UnityEngine.Screen.SetResolution), new Type[] { typeof(int), typeof(int), typeof(bool), typeof(int) })]
            [HarmonyPatch(typeof(Screen), nameof(UnityEngine.Screen.SetResolution), new Type[] { typeof(int), typeof(int), typeof(FullScreenMode), typeof(int) })] // Custom Resolution
            [HarmonyPostfix]
            public static void UpdateScaling(ref int __0, ref int __1)
            {
                if (!bCustomResolution.Value)
                {
                    NewAspectRatio = (float)__0 / (float)__1;
                    AspectMultiplier = NewAspectRatio / DefaultAspectRatio;

                    NewHorFloat = 640 * AspectMultiplier;
                    NewHor = (int)Mathf.CeilToInt(NewHorFloat);

                    Log.LogInfo($"Current res = {__0}x{__1}.");
                }

                if (NewAspectRatio > DefaultAspectRatio)
                {
                    ScalingManager.scalingType = ScalingManager.ScalingType.distort;

                    List<Camera> cameras = new List<Camera>();
                    for (int i = 0; i < SceneManager.sceneCount; i++)
                    {
                        var s = SceneManager.GetSceneAt(i);
                        if (s.isLoaded)
                        {
                            var allGameObjects = s.GetRootGameObjects();
                            for (int j = 0; j < allGameObjects.Length; j++)
                            {
                                var go = allGameObjects[j];
                                cameras.AddRange(go.GetComponentsInChildren<Camera>(true));
                            }
                        }
                    }

                    foreach (Camera cam in cameras)
                    {
                        if (cam.targetTexture != null && cam.targetTexture.name == "MainScreen")
                        {
                            RenderTexture old = cam.targetTexture;
                            old.Release();
                            old.width = NewHor;
                            old.height = 360;
                            cam.ResetAspect();
                            Log.LogInfo($"Released and resized render texture for MainScreen ({NewHor}x360).");
                        }
                    }

                    var diagcam = GameObject.Find("Diag/Effects Camera").GetComponent<Camera>();
                    diagcam.aspect = NewAspectRatio;
                    Log.LogInfo($"Adjust Diag/Effects aspect ratio.");
                }       
            }

            // Release and resize Enemy FX Camera render texture
            [HarmonyPatch(typeof(EnemyManager), nameof(EnemyManager.OnEnable))]
            [HarmonyPostfix]
            public static void EnemyFXFix()
            {  
                if (NewAspectRatio > DefaultAspectRatio)
                {
                    var enemyFX = GameObject.Find("Enemy FX Camera").GetComponent<Camera>();

                    if (enemyFX.targetTexture != null)
                    {
                        RenderTexture enemyrt = enemyFX.targetTexture;
                        enemyrt.Release();
                        enemyrt.width = NewHor;
                        enemyrt.height = 360;
                        enemyFX.ResetAspect();
                        Log.LogInfo($"Released and resized render texture for Enemy FX Camera.");
                    }   
                }
            }

            // Adjust size of input canvas during "Events"
            [HarmonyPatch(typeof(EventScreen3DCam), nameof(EventScreen3DCam.OnEnable))]
            [HarmonyPostfix]
            public static void InputFix()
            {
                if (NewAspectRatio > DefaultAspectRatio)
                {
                    var pivot = GameObject.Find("EventUICanvas/Pivot").GetComponent<RectTransform>();
                    if (pivot && pivot.gameObject.transform.localScale.x == (float)640)
                    {
                        Vector3 pivotscale = pivot.gameObject.transform.localScale;
                        pivotscale.x *= AspectMultiplier;
                        pivot.gameObject.transform.localScale = pivotscale;

                        Vector3 pivotpos = pivot.gameObject.transform.localPosition;
                        pivotpos.x *= AspectMultiplier;
                        pivot.gameObject.transform.localPosition = pivotpos;

                        Log.LogInfo($"Rescaled input transform.");
                    }
                }
            }

            // Resize black bars in skippable cutscenes
            [HarmonyPatch(typeof(SkippableCutscene), nameof(SkippableCutscene.Check))]
            [HarmonyPostfix]
            public static void CutsceneBlackBars(SkippableCutscene __instance)
            {
                if (NewAspectRatio > DefaultAspectRatio)
                {
                    List<SpriteRenderer> spriterenderers = new List<SpriteRenderer>();
                    for (int i = 0; i < SceneManager.sceneCount; i++)
                    {
                        var s = SceneManager.GetSceneAt(i);
                        if (s.isLoaded)
                        {
                            var allGameObjects = s.GetRootGameObjects();
                            for (int j = 0; j < allGameObjects.Length; j++)
                            {
                                var go = allGameObjects[j];
                                spriterenderers.AddRange(go.GetComponentsInChildren<SpriteRenderer>(true));
                            }
                        }
                    }

                    foreach (SpriteRenderer spriterender in spriterenderers)
                    {
                        if (spriterender != null && spriterender.sprite != null && spriterender.sprite.name == "ultrawide_bars")
                        {
                            spriterender.gameObject.transform.localScale = new Vector3((float)1 * AspectMultiplier, (float)1, (float)1);
                            Log.LogInfo($"Resized ultrawide bars.");
                        }

                    }
                }  
            }

            // Resize black bars in cutscenes
            [HarmonyPatch(typeof(BlackBars), nameof(BlackBars.On))]
            [HarmonyPatch(typeof(BlackBars), nameof(BlackBars.FadeIn), new Type[] { } )]
            [HarmonyPatch(typeof(BlackBars), nameof(BlackBars.FadeIn), new Type[] { typeof(float) })]
            [HarmonyPatch(typeof(BlackBars), nameof(BlackBars.fadeIn), new Type[] { typeof(float) })]
            [HarmonyPostfix]
            public static void BlackBarsFix(BlackBars __instance)
            {
                if (NewAspectRatio > DefaultAspectRatio)
                {
                    if (BlackBars.instance != null)
                    {
                        var topbar = BlackBars.instance.barTop.sizeDelta;
                        var botbar = BlackBars.instance.barBottom.sizeDelta;

                        if (topbar.x == 650 && botbar.x == 650)
                        {
                            topbar.x *= AspectMultiplier;
                            botbar.x *= (float)1 * AspectMultiplier;

                            BlackBars.instance.barTop.sizeDelta = topbar;
                            BlackBars.instance.barBottom.sizeDelta = botbar;
                        }
                    }
                }    
            }

            // Resize white fades/letterboxing
            [HarmonyPatch(typeof(MainFader), nameof(MainFader.OnEnable))]
            [HarmonyPostfix]
            public static void FadesFix(MainFader __instance)
            {
                if (NewAspectRatio > DefaultAspectRatio)
                {
                    List<Image> images = new List<Image>();
                    for (int i = 0; i < SceneManager.sceneCount; i++)
                    {
                        var s = SceneManager.GetSceneAt(i);
                        if (s.isLoaded)
                        {
                            var allGameObjects = s.GetRootGameObjects();
                            for (int j = 0; j < allGameObjects.Length; j++)
                            {
                                var go = allGameObjects[j];
                                images.AddRange(go.GetComponentsInChildren<Image>(true));
                            }
                        }
                    }

                    foreach (Image image in images)
                    {
                        if (image != null && image.sprite != null && image.sprite.name == "ultrawide_bars" || image != null && image.sprite != null && image.sprite.name == "WhitePixel")
                        {
                            image.gameObject.transform.localScale = new Vector3((float)1 * AspectMultiplier, (float)1, (float)1);
                            Log.LogInfo($"Resized ultrawide bars and or white overlay.");
                        }

                    }
                }
            }
        }

        [HarmonyPatch]
        public class IntroSkipPatch
        {
            // Skip intro cards
            [HarmonyPatch(typeof(OpeningCards), nameof(OpeningCards.Start))]
            [HarmonyPostfix]
            public static void IntroSkip(OpeningCards __instance)
            {
                __instance.Skip();
                Log.LogInfo($"Skipped intro.");
            }
        }
    }
}
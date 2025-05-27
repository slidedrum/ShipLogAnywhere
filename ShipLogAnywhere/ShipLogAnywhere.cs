using HarmonyLib;
using OWML.Common;
using OWML.ModHelper;
using System;
using System.Collections.Generic;
using System.EnterpriseServices.CompensatingResourceManager;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace ShipLogAnywhere;

public class ShipLogAnywhere : ModBehaviour
{
    public static ShipLogAnywhere Instance;
    public static ShipLogController shipLogController;
    private ScreenPrompt _OpenPrompt;
    private bool InGame => LoadManager.GetCurrentScene() == OWScene.SolarSystem || LoadManager.GetCurrentScene() == OWScene.EyeOfTheUniverse;
    GameObject mirrorCamObj;
    Camera mirrorCam;
    GameObject mirrorCanvasObj;
    Canvas canvas;
    GameObject rawImageObj;
    RawImage rawImage;
    float offsetDistance = 0.5f;
    float orthographicSize = 0.51f;
    float timeSinceSlowUpdate = 0f;

    public void Awake()
    {
        Instance = this;
    }
    private void Update()
    {
        timeSinceSlowUpdate += Time.deltaTime;
        if (timeSinceSlowUpdate > 0.05)
        {
            SlowUpdate();
            timeSinceSlowUpdate = 0;
        }
        if (shipLogController)
        {
            if (OWInput.IsNewlyPressed(InputLibrary.autopilot, InputMode.Character) && !shipLogController._usingShipLog)
            {
                ShowShipComputer();
            }
        }

    }
    public void OnStartSceneLoad(OWScene previousScene, OWScene newScene)
    {
        if (previousScene == OWScene.SolarSystem || previousScene == OWScene.EyeOfTheUniverse)
        {
            Locator.GetPromptManager().RemoveScreenPrompt(_OpenPrompt);
        }
    }
    private void SlowUpdate()
    {
        if (shipLogController)
        {
            mirrorCam.Render();
        }
        if (_OpenPrompt != null)
        {
            _OpenPrompt.SetVisibility(true);
        }
    }

    public void Start()
    {
        new Harmony("SlideDrum.ShipLogAnywhere").PatchAll(Assembly.GetExecutingAssembly());
        OnCompleteSceneLoad(OWScene.TitleScreen, OWScene.TitleScreen); // We start on title screen
        LoadManager.OnCompleteSceneLoad += OnCompleteSceneLoad;
    }
    public void ShowShipComputer()
    {

        shipLogController.enabled = true;
        base.enabled = true;
        shipLogController._usingShipLog = true;
        shipLogController._exiting = false;
        shipLogController._shipLogCanvas.gameObject.SetActive(true);
        shipLogController._canvasAnimator.AnimateTo(1f, Vector3.one * 0.001f, 0.5f, null, false);
        Locator.GetToolModeSwapper().UnequipTool();
        Locator.GetFlashlight().TurnOff(false);
        Locator.GetPromptManager().AddScreenPrompt(shipLogController._exitPrompt, shipLogController._upperRightPromptListMap, TextAnchor.MiddleRight, -1, false, false);
        Locator.GetPromptManager().AddScreenPrompt(shipLogController._exitPrompt, shipLogController._upperRightPromptListDetective, TextAnchor.MiddleRight, -1, false, false);
        List<ShipLogFact> list = shipLogController.BuildRevealQueue();
        if (list.Count > 0)
        {
            shipLogController._currentMode = shipLogController._detectiveMode;
        }
        if (!PlayerData.GetDetectiveModeEnabled() || !PlayerData.GetPersistentCondition("HAS_USED_SHIPLOG"))
        {
            shipLogController._currentMode = shipLogController._mapMode;
        }
        shipLogController._oneShotSource.PlayOneShot(global::AudioType.ShipLogBootUp, 1f);
        shipLogController._ambienceSource.FadeIn(Locator.GetAudioManager().GetSingleAudioClip(global::AudioType.ShipLogBootUp, true).length, true, false, 1f);


        //GlobalMessenger.FireEvent("EnterShipComputer");
        //Call firevent manually to exclude player camera
        IDictionary<string, GlobalMessenger.EventData> dictionary = GlobalMessenger.eventTable;
        lock (dictionary)
        {
            GlobalMessenger.EventData eventData;
            if (GlobalMessenger.eventTable.TryGetValue("EnterShipComputer", out eventData))
            {
                if (eventData.isInvoking)
                {
                    throw new InvalidOperationException("GlobalMessenger does not support recursive FireEvent calls to the same eventType.");
                }
                eventData.isInvoking = true;
                eventData.temp.AddRange(eventData.callbacks);
                for (int i = 0; i < eventData.temp.Count; i++)
                {
                    try
                    {
                        if (!(eventData.temp[i].Target is PlayerCameraController)) //extra check to avoid notifying player camera
                            eventData.temp[i]();
                    }
                    catch (Exception ex)
                    {
                        Debug.LogException(ex);
                    }
                }
                eventData.temp.Clear();
                eventData.isInvoking = false;
            }
        }


        shipLogController._splashScreen.OnEnterComputer();
        if (PlayerData.GetDetectiveModeEnabled())
        {
            shipLogController._detectiveMode.OnEnterComputer();
        }
        shipLogController._mapMode.OnEnterComputer();
        shipLogController._currentMode.EnterMode("", list);

        mirrorCamObj.SetActive(true);
        mirrorCanvasObj.SetActive(true);


    }
    public void setupRenderTexture()
    {
        RenderTexture mirrorTexture = new RenderTexture((int)(Screen.width * 0.5), (int)(Screen.height * 0.5), 0);
        mirrorTexture.Create();
        Transform canvasTransform = shipLogController._shipLogCanvas.transform;
        mirrorCamObj = new GameObject("MirrorCamera");
        mirrorCam = mirrorCamObj.AddComponent<Camera>();
        mirrorCanvasObj = new GameObject("MirrorDisplayCanvas");
        canvas = mirrorCanvasObj.AddComponent<Canvas>();
        rawImageObj = new GameObject("MirrorDisplay");
        rawImage = rawImageObj.AddComponent<RawImage>();

        int canvasLayer = shipLogController._shipLogCanvas.gameObject.layer;

        mirrorCam.cullingMask = 1 << canvasLayer;
        //mirrorCam.cullingMask = -1;
        mirrorCam.orthographic = true;
        mirrorCam.targetTexture = mirrorTexture;
        mirrorCam.targetTexture = mirrorTexture;

        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        mirrorCanvasObj.AddComponent<CanvasScaler>();
        mirrorCanvasObj.AddComponent<GraphicRaycaster>();
        mirrorCam.orthographicSize = orthographicSize;
        mirrorCam.farClipPlane = offsetDistance*2;
        rawImageObj.transform.SetParent(mirrorCanvasObj.transform, false);
        rawImage.texture = mirrorTexture;

        RectTransform rt = rawImage.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        RectTransform parentRt = rt.parent.GetComponent<RectTransform>();
        float width = parentRt.rect.width * 0.5f;
        float height = parentRt.rect.height * 0.5f;
        rt.sizeDelta = new Vector2(width, height);
        Vector3 mirrorCamPosition = canvasTransform.position - canvasTransform.forward * offsetDistance;
        mirrorCamObj.transform.position = mirrorCamPosition;
        mirrorCamObj.transform.LookAt(canvasTransform.position, canvasTransform.transform.up);
        mirrorCamObj.transform.SetParent(canvasTransform, worldPositionStays: true);

        mirrorCam.enabled = false;
    }
    public void OnCompleteSceneLoad(OWScene previousScene, OWScene newScene)
    {
        if (newScene != OWScene.SolarSystem) return;
        ModHelper.Console.WriteLine("Loaded into solar system!", MessageType.Success);
        ModHelper.Console.WriteLine("Looking for ship log!", MessageType.Info);
        GameObject shipLogObject = GameObject.Find("ShipLog");
        if (shipLogObject == null)
        {
            ModHelper.Console.WriteLine("Could not find ship log gobject!", MessageType.Error);
            return;
        }
        else
        {
            shipLogController = shipLogObject.GetComponent<ShipLogController>();
            if (shipLogController == null)
            {
                ModHelper.Console.WriteLine("Could not find ship log!", MessageType.Error);
                return;
            }
            else
            {
                ModHelper.Console.WriteLine("Found ship log!", MessageType.Success);
            }
        }
        setupRenderTexture();
        _OpenPrompt = new ScreenPrompt(InputLibrary.autopilot, "View ship log", 0, ScreenPrompt.DisplayState.Normal);
        ModHelper.Events.Unity.RunWhen(() => Locator._promptManager != null, () =>
        {
            Locator.GetPromptManager().AddScreenPrompt(_OpenPrompt, PromptPosition.UpperRight, true);
            ModHelper.Console.WriteLine("Sent screen prompt", MessageType.Info);
        });
        GlobalMessenger.AddListener("ExitShipComputer", OnExitShipComputer);
    }
    public void OnExitShipComputer()
    {
        mirrorCamObj.SetActive(false);
        mirrorCanvasObj.SetActive(false);
    }
}


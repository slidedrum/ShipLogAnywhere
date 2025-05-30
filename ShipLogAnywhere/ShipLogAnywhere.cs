﻿using HarmonyLib;
using OWML.Common;
using OWML.ModHelper;
using OWML.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;


namespace ShipLogAnywhere;

public class ShipLogAnywhere : ModBehaviour
{
    public static ShipLogAnywhere Instance;
    public static ShipLogController shipLogController;
    public static IModHelper modHelper;
    string _selectedInputName = "Autopilot";
    private bool _keybindCompat;
    Camera mirrorCam;
    float resScale = 0.60f;
    float cameraOffsetDistance = 0.5f;
    float orthographicSize = 0.33f;
    public static float gobjectDistanceToCamera = 1.5f;
    private bool InGame => LoadManager.GetCurrentScene() == OWScene.SolarSystem || LoadManager.GetCurrentScene() == OWScene.EyeOfTheUniverse;

    Dictionary<float, List<Callback>> slowUpdates = new Dictionary<float, List<Callback>>();
    private Dictionary<float, float> _elapsedTimeByInterval = new Dictionary<float, float>();

    public ItemType PortableShipLogType { get; private set; }

    public static string _mode;
    public static bool _requireSuit;
    private PortableShipLogTool portableShipLogTool;
    private PortableShipLogItem portableShipLogItem;
    private ScreenPrompt _openPrompt;
    private HashSet<ScreenPromptElement> controllerConflictingPrompts;
    private HashSet<ScreenPromptElement> keyboardConflictingPrompts;

    public void addSlowUpdate(float interval, Callback callback)
    {
        if (slowUpdates.TryGetValue(interval, out var callbackList))
        {
            if (callbackList.Contains(callback))
                ModHelper.Console.WriteLine($"Duplicate callback {callback.Target}", MessageType.Warning);
            else
                callbackList.Add(callback);
        }
        else
            slowUpdates[interval] = new List<Callback> { callback };
    }
    private void Update()
    {

        foreach (var kvp in slowUpdates)
        {
            float interval = kvp.Key;
            List<Callback> callbacks = kvp.Value;

            if (!_elapsedTimeByInterval.ContainsKey(interval))
                _elapsedTimeByInterval[interval] = 0f;

            _elapsedTimeByInterval[interval] += Time.deltaTime;

            if (_elapsedTimeByInterval[interval] >= interval)
            {
                foreach (Callback callback in callbacks)
                {
                    callback?.Invoke();
                }
                _elapsedTimeByInterval[interval] = 0f;
            }
        }
        if (shipLogController != null && InGame && _openPrompt != null)
        {
            _openPrompt.SetVisibility(false);
            if (
                ((_mode == "Tool" && portableShipLogTool != null) || (_mode == "Item" && portableShipLogItem != null && Locator.GetToolModeSwapper().GetItemCarryTool().GetHeldItemType() == PortableShipLogType)) &&
                (_mode == "Item" || !(!Locator.GetPlayerSuit().IsWearingSuit() && ShipLogAnywhere._requireSuit)) &&
                !(!shipLogController || !shipLogController.gameObject.activeInHierarchy || shipLogController._damaged) &&
                !PlayerState._usingShipComputer &&
                OWInput.IsInputMode(InputMode.Character))
            {
                _openPrompt.SetVisibility(true);
                if (OWInput.IsNewlyPressed(GetSelectedInput(), InputMode.Character) && !otherPromptWithSameKeyVisible())
                {
                    if (_mode == "Tool" && portableShipLogTool != null)
                    {
                        portableShipLogTool.EquipTool();
                    }
                    else if (_mode == "Item" && portableShipLogItem != null && Locator.GetToolModeSwapper().GetItemCarryTool().GetHeldItemType() == PortableShipLogType)
                    {
                        portableShipLogItem.lookAtLog();
                    }
                }
            }
        }
        if (portableShipLogItem != null)
            portableShipLogItem.Update();
        if (portableShipLogTool != null)
            portableShipLogTool.update();
    }

    private void SlowUpdate60fps()
    {
        if (mirrorCam && ((_mode == "Item" && portableShipLogItem != null && Locator.GetToolModeSwapper().GetItemCarryTool().GetHeldItemType() == PortableShipLogType && portableShipLogItem._looking)) || _mode == "Tool" && portableShipLogTool != null && portableShipLogTool._isEquipped)
        {
            mirrorCam.Render();
        }
    }

    public void Start()
    {
        new Harmony("SlideDrum.ShipLogAnywhere").PatchAll(Assembly.GetExecutingAssembly());
        OnCompleteSceneLoad(OWScene.TitleScreen, OWScene.TitleScreen);
        LoadManager.OnCompleteSceneLoad += OnCompleteSceneLoad;
        addSlowUpdate(0.016f, SlowUpdate60fps);
        ModHelper.Console.WriteLine("Ship log anywhere starting up!", MessageType.Success);
        Instance = this;
        modHelper = ModHelper;
        GlobalMessenger.AddListener("FinishOpenEyes", setupPrompt);
        PortableShipLogType = EnumUtils.Create<ItemType>("PortableShipLog");

    }
    public override void Configure(IModConfig config)
    {
        _mode = config.GetSettingsValue<string>("mode");
        _requireSuit = config.GetSettingsValue<bool>("requireSuit");
        _selectedInputName = config.GetSettingsValue<string>("inputName");
        _keybindCompat = config.GetSettingsValue<bool>("keybindCompat");
        if (_openPrompt != null)
            setupPrompt();
    }
    public static IInputCommands GetSelectedInput()
    {
        return Instance._selectedInputName switch
        {
            "Autopilot" => InputLibrary.autopilot,
            "Interact" => InputLibrary.interact,
            "Alt Interact" => InputLibrary.interactSecondary,
            "Free Look" => InputLibrary.freeLook,
            "Tool Primary" => InputLibrary.toolActionPrimary,
            "Tool Secondary" => InputLibrary.toolActionSecondary,
            _ => null,
        };
    }
    public void setupShipLogObject()
    {
        if (_mode != "Tool" && _mode != "Item")
        {
            modHelper.Console.WriteLine("Something went horribly wrong. Unknown mode.", MessageType.Error);
            return;
        }
        ModHelper.Console.WriteLine("Setting up ship log object", MessageType.Info);


        float targetAspect = 58f / 37f;
        int maxWidth = (int)(Screen.width * resScale);
        int maxHeight = (int)(Screen.height * resScale);
        int width = maxWidth;
        int height = (int)(width / targetAspect);
        if (height > maxHeight)
        {
            height = maxHeight;
            width = (int)(height * targetAspect);
        }

        RenderTexture mirrorTexture = new RenderTexture(width, height, 0);
        mirrorTexture.Create();

        Transform canvasTransform = shipLogController._shipLogCanvas.transform;
        GameObject mirrorCamObj = new GameObject("MirrorCamera");
        mirrorCam = mirrorCamObj.AddComponent<Camera>();

        int canvasLayer = shipLogController._shipLogCanvas.gameObject.layer;
        mirrorCam.cullingMask = 1 << canvasLayer;
        mirrorCam.orthographic = true;
        mirrorCam.targetTexture = mirrorTexture;
        mirrorCam.orthographicSize = orthographicSize;
        mirrorCam.farClipPlane = cameraOffsetDistance * 2;

        Vector3 mirrorCamPosition = canvasTransform.position - canvasTransform.forward * cameraOffsetDistance;
        mirrorCamObj.transform.position = mirrorCamPosition;
        mirrorCamObj.transform.LookAt(canvasTransform.position, canvasTransform.transform.up);
        mirrorCamObj.transform.SetParent(canvasTransform, worldPositionStays: true);
        mirrorCam.enabled = (_mode == "Item");

        GameObject baseCube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        baseCube.name = "ShipLogMirrorBase";
        baseCube.transform.localRotation = Quaternion.identity;
        float baseHeight = 1f;
        float baseWidth = baseHeight * targetAspect;
        baseCube.transform.localScale = new Vector3(baseWidth, baseHeight, 1f);

        if (_mode == "Tool")
        {
            GameObject cubePivot = new GameObject("ShipLogMirrorPivot");
            cubePivot.transform.SetParent(GameObject.Find("MainToolRoot").transform);
            cubePivot.transform.localPosition = Vector3.zero;
            portableShipLogTool = cubePivot.AddComponent<PortableShipLogTool>();
            baseCube.transform.SetParent(cubePivot.transform);
            baseCube.transform.localPosition = new Vector3(0, 0f, -gobjectDistanceToCamera);
            baseCube.GetComponent<Collider>().enabled = false;
            //cubePivot.GetComponent<Collider>().enabled = false;
        }
        else if (_mode == "Item")
        {
            baseCube.transform.SetParent(Locator._shipBody.transform);
            baseCube.transform.localPosition = new Vector3(1.2f, 2f, - 4f);
            baseCube.transform.localRotation = Quaternion.Euler(0f, 2.284f, 180f);
            portableShipLogItem = baseCube.AddComponent<PortableShipLogItem>();
            portableShipLogItem.transform.localScale *= 0.3f;
            foreach (var collider in portableShipLogItem._colliders)
            {
                if (collider != null)
                {
                    collider.GetCollider().isTrigger = true;
                }
            }
        }
        
        GameObject screenQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        screenQuad.name = "ShipLogScreenFace";
        screenQuad.transform.localPosition = new Vector3(0, 0, 0.51f);
        screenQuad.transform.localRotation = Quaternion.Euler(0, 180f, 0);
        screenQuad.transform.localScale = Vector3.one * 0.9f;
        screenQuad.GetComponent<Collider>().enabled = false;
        screenQuad.transform.SetParent(baseCube.transform, false);

        Material displayMaterial = new Material(Shader.Find("Unlit/Texture"));
        displayMaterial.mainTexture = mirrorTexture;
        Renderer screenRenderer = screenQuad.GetComponent<Renderer>();
        screenRenderer.material = displayMaterial;
        screenRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }
    public void OnCompleteSceneLoad(OWScene previousScene, OWScene newScene)
    {
        _openPrompt = null;
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

                ModHelper.Events.Unity.RunWhen(() => GameObject.Find("MainToolRoot") != null, () =>
                {
                    setupShipLogObject();
                });
            }
        }

    }
    public bool otherPromptWithSameKeyVisible()
    {
        if (!_keybindCompat)
            return false;
        checkForConflictingPrompts();
        var conflictingPrompts = OWInput.UsingGamepad() ? controllerConflictingPrompts : keyboardConflictingPrompts;
        ScreenPromptElement visiblePrompt = conflictingPrompts.FirstOrDefault(prompt => prompt.isActiveAndEnabled);

        if (visiblePrompt != null)
        {
            ModHelper.Console.WriteLine($"Conflicting prompt {visiblePrompt._textStr}", MessageType.Warning);
            return true;
        }
        return false;
    }

    public void checkForConflictingPrompts()
    {
        controllerConflictingPrompts = new HashSet<ScreenPromptElement>();
        keyboardConflictingPrompts = new HashSet<ScreenPromptElement>();

        var input = GetSelectedInput(); // Store selected input once
        var promptManager = Locator.GetPromptManager(); // Store prompt manager once

        foreach (PromptPosition position in Enum.GetValues(typeof(PromptPosition)))
        {
            ScreenPromptList screenPromptList = promptManager.GetScreenPromptList(position);
            if (screenPromptList?._listPromptUiElements == null) continue;

            foreach (var prompt in screenPromptList._listPromptUiElements)
            {
                var promptData = prompt?.GetPromptData();
                if (promptData == null || promptData == _openPrompt) continue;

                foreach (IInputCommands command in promptData.GetInputCommandList())
                {
                    if (command.HasSameBinding(input, true))
                    {
                        controllerConflictingPrompts.Add(prompt);
                    }
                    if (command.HasSameBinding(input, false))
                    {
                        keyboardConflictingPrompts.Add(prompt);
                    }
                }
            }
        }
    }
    private void setupPrompt()
    {
        checkForConflictingPrompts();
        Locator.GetPromptManager().RemoveScreenPrompt(_openPrompt);
        _openPrompt = null; ;
        ModHelper.Console.WriteLine("Setting up prompt");
        _openPrompt = new ScreenPrompt(GetSelectedInput(), "Open ship log");
        Locator.GetPromptManager().AddScreenPrompt(_openPrompt, PromptPosition.UpperRight, false);
    }

    public void pickyFireEvent(string eventType, List<object> exclusions)
    {
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
                        if (!exclusions.Contains(eventData.temp[i].Target)) //extra check to avoid notifying
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
    }
}
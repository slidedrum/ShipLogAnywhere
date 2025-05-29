using HarmonyLib;
using OWML.Common;
using OWML.Logging;
using OWML.ModHelper;
using Steamworks;
using System;
using System.Collections;
using System.Collections.Generic;
using System.EnterpriseServices.CompensatingResourceManager;
using System.Linq;
using System.Reflection;
using Unity.Collections;
using UnityEngine;
using UnityEngine.UI;


namespace ShipLogAnywhere;

public class ShipLogAnywhere : ModBehaviour
{
    public static ShipLogAnywhere Instance;
    public static ShipLogController shipLogController;
    public static IModHelper modHelper;
    string _selectedInputName = "Autopilot";
    Camera mirrorCam;
    float resScale = 0.60f;
    float cameraOffsetDistance = 0.5f;
    float orthographicSize = 0.33f;
    public static float gobjectDistanceToCamera = 1.5f;
    private bool InGame => LoadManager.GetCurrentScene() == OWScene.SolarSystem || LoadManager.GetCurrentScene() == OWScene.EyeOfTheUniverse;

    Dictionary<float, List<Callback>> slowUpdates = new Dictionary<float, List<Callback>>();
    private Dictionary<float, float> _elapsedTimeByInterval = new Dictionary<float, float>();

    public static string _mode;
    public static bool _requireSuit;
    private PortableShipLogTool portableShipLogTool;
    private ScreenPrompt _openPrompt;

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
            if (!(!Locator.GetPlayerSuit().IsWearingSuit() && ShipLogAnywhere._requireSuit) && 
                !(!shipLogController || !shipLogController.gameObject.activeInHierarchy || shipLogController._damaged) && 
                !PlayerState._usingShipComputer && 
                !PlayerState._insideShip &&
                !otherPromptWithSameKeyVisible())
            {
                _openPrompt.SetVisibility(true);
                if (OWInput.IsNewlyPressed(GetSelectedInput(), InputMode.Character))
                    portableShipLogTool.EquipTool();
            }
        }
    }

    private void SlowUpdate60fps()
    {
        if (mirrorCam)
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
    }
    public override void Configure(IModConfig config)
    {
        _mode = config.GetSettingsValue<string>("mode");
        _requireSuit = config.GetSettingsValue<bool>("requireSuit");
        _selectedInputName = config.GetSettingsValue<string>("inputName");
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
        //This will be dramatically simplfied once I get a proper model for it and can set up a prefab.
        //But for now this will all be done in code.
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
        mirrorCam.enabled = false;

        // Create a pivot object for rotation
        GameObject cubePivot = new GameObject("ShipLogMirrorPivot");
        cubePivot.transform.SetParent(GameObject.Find("MainToolRoot").transform);
        cubePivot.transform.localPosition = new Vector3(0, 0f, 0);
        cubePivot.transform.localRotation = Quaternion.identity;
        portableShipLogTool = cubePivot.AddComponent<PortableShipLogTool>();
        //cubePivot.SetActive(false);

        GameObject baseCube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        baseCube.name = "ShipLogMirrorBase";
        baseCube.transform.SetParent(cubePivot.transform);
        baseCube.transform.localPosition = new Vector3(0, 0f, -gobjectDistanceToCamera);
        baseCube.transform.localRotation = Quaternion.identity;
        float aspectRatio = 58f / 37f;
        float baseHeight = 1f;
        float baseWidth = baseHeight * aspectRatio;
        baseCube.transform.localScale = new Vector3(baseWidth, baseHeight, 1f);
        baseCube.GetComponent<Collider>().enabled = false;

        GameObject screenQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        screenQuad.name = "ShipLogScreenFace";
        screenQuad.transform.localPosition = new Vector3(0, 0, 0.51f);
        screenQuad.transform.localRotation = Quaternion.Euler(0, 180f, 0);
        float heightScale = 0.9f;
        float widthScale = heightScale * (16f / 9f);
        //screenQuad.transform.localScale = new Vector3(widthScale, heightScale, 1f);  //uncomment this when using proper object
        screenQuad.transform.localScale = Vector3.one * 0.9f;
        screenQuad.GetComponent<Collider>().enabled = false;
        screenQuad.transform.SetParent(baseCube.transform, false);

        // Assign the render texture to the quad's material
        Material displayMaterial = new Material(Shader.Find("Unlit/Texture"));
        displayMaterial.mainTexture = mirrorTexture;
        screenQuad.GetComponent<Renderer>().material = displayMaterial;
        screenQuad.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

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
        return false;
    }
    private void setupPrompt()
    {
        Locator.GetPromptManager().RemoveScreenPrompt(_openPrompt);
        _openPrompt = null;;
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
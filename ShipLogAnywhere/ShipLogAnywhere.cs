using HarmonyLib;
using OWML.Common;
using OWML.ModHelper;
using System;
using System.Collections;
using System.Collections.Generic;
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
    private ScreenPrompt _OpenPrompt;
    private bool InGame => LoadManager.GetCurrentScene() == OWScene.SolarSystem || LoadManager.GetCurrentScene() == OWScene.EyeOfTheUniverse;
    GameObject mirrorCamObj;
    Camera mirrorCam;
    Material displayMaterial;
    GameObject baseCube;
    GameObject screenQuad;
    float offsetDistance = 0.5f;
    float orthographicSize = 0.33f;
    float gobjectDistanceToCamera = 1.2f;
    Dictionary<float, List<Callback>> slowUpdates = new Dictionary<float, List<Callback>>();
    private Dictionary<float, float> _elapsedTimeByInterval = new Dictionary<float, float>();

    public void Awake()
    {
        Instance = this;
    }
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
        if (shipLogController & InGame)
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
    private void SlowUpdate30fps()
    {
        if (shipLogController)
        {
            mirrorCam.Render();
            //mirrorCam.orthographicSize = orthographicSize;
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
        addSlowUpdate(0.016f,SlowUpdate30fps);
    }
    public void ShowShipComputer()
    {
        Camera cam = Camera.main;
        Transform camTransform = cam.transform;

        // Final position for the object

        Vector3 offsetPos= camTransform.position + -camTransform.up * gobjectDistanceToCamera;
        Quaternion lookRot = Quaternion.LookRotation(camTransform.position - offsetPos, camTransform.forward);
        baseCube.transform.rotation = lookRot;
        baseCube.transform.position = offsetPos;
        offsetPos = camTransform.position + camTransform.forward * gobjectDistanceToCamera;
        lookRot = Quaternion.LookRotation(camTransform.position - offsetPos, camTransform.up);

        MoveObjectSmoothly(baseCube.transform, offsetPos, lookRot, 1f);


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
    }
    public void setupShipLogObject()
    {
        // Create the render texture
        RenderTexture mirrorTexture = new RenderTexture((int)(Screen.width * 0.5f), (int)(Screen.height * 0.5f), 0);
        mirrorTexture.Create();


        // Get the ShipLog canvas transform
        Transform canvasTransform = shipLogController._shipLogCanvas.transform;

        // Set up the mirror camera
        mirrorCamObj = new GameObject("MirrorCamera");
        mirrorCam = mirrorCamObj.AddComponent<Camera>();

        int canvasLayer = shipLogController._shipLogCanvas.gameObject.layer;
        mirrorCam.cullingMask = 1 << canvasLayer;
        mirrorCam.orthographic = true;
        mirrorCam.targetTexture = mirrorTexture;
        mirrorCam.orthographicSize = orthographicSize;
        mirrorCam.farClipPlane = offsetDistance * 2;
        

       // Position the mirror camera behind the canvas
       Vector3 mirrorCamPosition = canvasTransform.position - canvasTransform.forward * offsetDistance;
        mirrorCamObj.transform.position = mirrorCamPosition;
        mirrorCamObj.transform.LookAt(canvasTransform.position, canvasTransform.transform.up);
        mirrorCamObj.transform.SetParent(canvasTransform, worldPositionStays: true);
        mirrorCam.enabled = false;


        // Create a cube as a dummy prefab base
        baseCube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        baseCube.name = "ShipLogMirrorBase";
        baseCube.transform.SetParent(Locator._timberHearth.transform);
        baseCube.transform.localPosition = new Vector3(0, 1.5f, 2);
        baseCube.transform.localRotation = Quaternion.identity;
        baseCube.transform.localScale = Vector3.one;
        baseCube.GetComponent<Collider>().enabled = false;

        // Create a quad and attach it to the cube as the "screen"
        screenQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        screenQuad.name = "ShipLogScreenFace";
        screenQuad.transform.SetParent(baseCube.transform);
        screenQuad.transform.localPosition = new Vector3(0, 0, 0.51f); // slightly in front of cube face
        screenQuad.transform.localRotation = Quaternion.Euler(0, 180f, 0);
        float heightScale = 0.9f;
        float widthScale = heightScale * (16f / 9f);
        screenQuad.transform.localScale = new Vector3(widthScale, heightScale, 1f);
        screenQuad.GetComponent<Collider>().enabled = false;

        // Assign the render texture to the quad's material
        displayMaterial = new Material(Shader.Find("Unlit/Texture"));
        displayMaterial.mainTexture = mirrorTexture;
        screenQuad.GetComponent<Renderer>().material = displayMaterial;
        screenQuad.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
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
        setupShipLogObject();
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
        
    }
    public void MoveObjectSmoothly(Transform targetTransform, Vector3 targetPosition, Quaternion targetRotation, float duration)
    {
        StartCoroutine(SmoothMoveAndRotate(targetTransform, targetPosition, targetRotation, duration));
    }

    private IEnumerator SmoothMoveAndRotate(Transform objTransform, Vector3 targetPos, Quaternion targetRot, float duration)
    {
        Vector3 startPos = objTransform.position;
        Quaternion startRot = objTransform.rotation;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            float t = elapsed / duration;
            objTransform.position = Vector3.Lerp(startPos, targetPos, t);
            objTransform.rotation = Quaternion.Slerp(startRot, targetRot, t);
            elapsed += Time.deltaTime;
            yield return null;
        }

        objTransform.position = targetPos;
        objTransform.rotation = targetRot;
    }
}

[HarmonyPatch]
public class PlayerCameraControllerPatch
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(PlayerCameraController), nameof(PlayerCameraController.OnEnterShipComputer))]
    public static bool PlayerCameraController_OnEnterShipComputer()
    {
        ShipLogAnywhere.Instance.ModHelper.Console.WriteLine("The used ship comooter!");
        return false;
    }
}
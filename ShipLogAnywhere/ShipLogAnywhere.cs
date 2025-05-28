using HarmonyLib;
using OWML.Common;
using OWML.ModHelper;
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
    private bool InGame => LoadManager.GetCurrentScene() == OWScene.SolarSystem || LoadManager.GetCurrentScene() == OWScene.EyeOfTheUniverse;
    GameObject mirrorCamObj;
    GameObject baseCube;
    GameObject cubePivot;
    GameObject screenQuad;
    Camera mirrorCam;
    Material displayMaterial;
    RenderTexture mirrorTexture;
    Transform canvasTransform;

    float resScale = 0.60f;
    float offsetDistance = 0.5f;
    float orthographicSize = 0.33f;
    float gobjectDistanceToCamera = 1.5f;

    Dictionary<float, List<Callback>> slowUpdates = new Dictionary<float, List<Callback>>();
    private Dictionary<float, float> _elapsedTimeByInterval = new Dictionary<float, float>();

    private bool _isPuttingAway;
    private Transform _stowTransform;
    private Transform _holdTransform;
    DampedSpringQuat _moveSpring = new DampedSpringQuat(15f,0.8f);
    private bool _isEquipped;
    private bool _isCentered;
    float _arrivalDegrees = 1f;
    private string _mode;
    private bool _requireSuit;

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

        if (HasEquipAnimation() && AllowEquipAnimation())
        {
            float num = (_isPuttingAway ? Time.unscaledDeltaTime : Time.deltaTime);
            Quaternion quaternion = (_isPuttingAway ? _stowTransform.localRotation : _holdTransform.localRotation);
            cubePivot.transform.localRotation = _moveSpring.Update(cubePivot.transform.localRotation, quaternion, num);
            float num2 = Quaternion.Angle(cubePivot.transform.localRotation, quaternion);
            if (_isEquipped && !_isCentered && num2 <= _arrivalDegrees)
            {
                _isCentered = true;
            }
            if (_isPuttingAway && num2 <= _arrivalDegrees)
            {
                _isEquipped = false;
                _isPuttingAway = false;
                cubePivot.SetActive(false);
                _moveSpring.ResetVelocity();
            }
        }

    }

    private bool AllowEquipAnimation()
    {
        return true;
    }

    private bool HasEquipAnimation()
    {
        return this._stowTransform != null && this._holdTransform != null;
    }

    private void SlowUpdate60fps()
    {
        if (mirrorCam)
        {
            mirrorCam.Render();
            //mirrorCam.orthographicSize = orthographicSize;
        }
    }

    public void Start()
    {
        new Harmony("SlideDrum.ShipLogAnywhere").PatchAll(Assembly.GetExecutingAssembly());
        OnCompleteSceneLoad(OWScene.TitleScreen, OWScene.TitleScreen);
        LoadManager.OnCompleteSceneLoad += OnCompleteSceneLoad;
        addSlowUpdate(0.016f, SlowUpdate60fps);
    }
    public override void Configure(IModConfig config)
    {
        _mode = config.GetSettingsValue<string>("mode");
        _requireSuit = config.GetSettingsValue<bool>("requireSuit");
    }
    public void ShowShipComputer()
    {

        if (_isEquipped)
            return;
        _isEquipped = true;
        _isPuttingAway = false;
        _isCentered = !HasEquipAnimation();
        if (HasEquipAnimation())
        {
            cubePivot.transform.localRotation = _stowTransform.localRotation;
        }
        cubePivot.SetActive(true);

        shipLogController.enabled = true;
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
        mirrorTexture = new RenderTexture(width, height, 0);
        mirrorTexture.Create();
        canvasTransform = shipLogController._shipLogCanvas.transform;
        mirrorCamObj = new GameObject("MirrorCamera");
        mirrorCam = mirrorCamObj.AddComponent<Camera>();

        int canvasLayer = shipLogController._shipLogCanvas.gameObject.layer;
        mirrorCam.cullingMask = 1 << canvasLayer;
        mirrorCam.orthographic = true;
        mirrorCam.targetTexture = mirrorTexture;
        mirrorCam.orthographicSize = orthographicSize;
        mirrorCam.farClipPlane = offsetDistance * 2;

        Vector3 mirrorCamPosition = canvasTransform.position - canvasTransform.forward * offsetDistance;
        mirrorCamObj.transform.position = mirrorCamPosition;
        mirrorCamObj.transform.LookAt(canvasTransform.position, canvasTransform.transform.up);
        mirrorCamObj.transform.SetParent(canvasTransform, worldPositionStays: true);
        mirrorCam.enabled = false;

        // Create a pivot object for rotation
        cubePivot = new GameObject("ShipLogMirrorPivot");
        cubePivot.transform.SetParent(GameObject.Find("MainToolRoot").transform);
        cubePivot.transform.localPosition = new Vector3(0, 0f,0);
        cubePivot.transform.localRotation = Quaternion.identity;
        cubePivot.SetActive(false);

        baseCube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        baseCube.name = "ShipLogMirrorBase";
        baseCube.transform.SetParent(cubePivot.transform);
        baseCube.transform.localPosition = new Vector3(0, 0f, -gobjectDistanceToCamera);
        baseCube.transform.localRotation = Quaternion.identity;
        float aspectRatio = 58f / 37f;
        float baseHeight = 1f;
        float baseWidth = baseHeight * aspectRatio;
        baseCube.transform.localScale = new Vector3(baseWidth, baseHeight, 1f);
        baseCube.GetComponent<Collider>().enabled = false;

        screenQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        screenQuad.name = "ShipLogScreenFace";
        screenQuad.transform.localPosition = new Vector3(0, 0, 0.51f); // slightly in front of cube face
        screenQuad.transform.localRotation = Quaternion.Euler(0, 180f, 0);
        float heightScale = 0.9f;
        float widthScale = heightScale * (16f / 9f);
        //screenQuad.transform.localScale = new Vector3(widthScale, heightScale, 1f);  //uncomment this when using proper object
        screenQuad.transform.localScale = Vector3.one * 0.9f;
        screenQuad.GetComponent<Collider>().enabled = false;
        screenQuad.transform.SetParent(baseCube.transform,false);

        // Assign the render texture to the quad's material        screenQuad.transform.SetParent(baseCube.transform);
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
                GameObject toolRoot = GameObject.Find("MainToolRoot");
                ModHelper.Events.Unity.RunWhen(() => GameObject.Find("MainToolRoot") != null, () =>
                {
                    Transform cameraRoot = GameObject.Find("MainToolRoot")?.transform;
                    if (cameraRoot != null)
                    {
                        _stowTransform = new GameObject("StowTransform").transform;
                        _stowTransform.SetParent(cameraRoot, false);

                        _holdTransform = new GameObject("HoldTransform").transform;
                        _holdTransform.SetParent(cameraRoot, false);

                        Vector3 offsetPos = cameraRoot.position + -cameraRoot.up * gobjectDistanceToCamera;
                        Quaternion lookRot = Quaternion.LookRotation(cameraRoot.position - offsetPos, cameraRoot.forward);
                        _stowTransform.transform.rotation = lookRot;
                        _stowTransform.transform.position = offsetPos;
                        offsetPos = cameraRoot.position + cameraRoot.forward * gobjectDistanceToCamera;
                        lookRot = Quaternion.LookRotation(cameraRoot.position - offsetPos, cameraRoot.up);
                        _holdTransform.transform.rotation = lookRot;
                        _holdTransform.transform.position = offsetPos;

                    }
                    else
                    {
                        ModHelper.Console.WriteLine("CameraRoot not found!", MessageType.Error);
                    }
                    setupShipLogObject();
                });
            }
        }
        GlobalMessenger.AddListener("ExitShipComputer", OnExitShipComputer);
    }
    public void OnExitShipComputer()
    {
        if (_isEquipped)
        {
            _isEquipped = false;
            if (!HasEquipAnimation())
            {
                _isCentered = false;
                cubePivot.SetActive(false);
                return;
            }
            if (!_isPuttingAway)
            {
                _isPuttingAway = true;
                _isCentered = false;
            }
        }
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
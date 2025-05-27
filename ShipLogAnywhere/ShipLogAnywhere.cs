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

    public void Awake()
	{
		Instance = this;
        // You won't be able to access OWML's mod helper in Awake.
        // So you probably don't want to do anything here.
        // Use Start() instead.
    }
    private void Update()
    {
        if (shipLogController != null)
        {

        }
    }

    public void Start()
	{
        
		// Starting here, you'll have access to OWML's mod helper.
		ModHelper.Console.WriteLine($"My mod {nameof(ShipLogAnywhere)} is loaded!", MessageType.Success);

		new Harmony("SlideDrum.ShipLogAnywhere").PatchAll(Assembly.GetExecutingAssembly());

		// Example of accessing game code.
		OnCompleteSceneLoad(OWScene.TitleScreen, OWScene.TitleScreen); // We start on title screen
		LoadManager.OnCompleteSceneLoad += OnCompleteSceneLoad;
	}
    public void ShowShipComputer()
    {
        shipLogController.enabled = true;
        //if (!shipLogController._timeFrozen && PlayerData.GetFreezeTimeWhileReadingShipLog() && !Locator.GetGlobalMusicController().IsEndTimesPlaying())
        //{
        //    shipLogController._timeFrozen = true;
        //    OWTime.Pause(OWTime.PauseType.Reading);
        //}
        base.enabled = true;
        shipLogController._usingShipLog = true;
        shipLogController._exiting = false;
        shipLogController._shipLogCanvas.gameObject.SetActive(true);
        shipLogController._canvasAnimator.AnimateTo(1f, Vector3.one * 0.001f, 0.5f, null, false);
        Locator.GetToolModeSwapper().UnequipTool();
        Locator.GetFlashlight().TurnOff(false);
        //shipLogController._attachPoint.AttachPlayer();
        //Locator.GetPlayerCamera().GetComponent<PlayerCameraController>().SnapToDegrees(0f, -16.9f, 100f, true);
        //AspectRatio aspectRatio = PlayerData.GetGraphicSettings().aspectRatio;
        //float num = 38.33f;
        //if (aspectRatio == AspectRatio.FOUR_THREE || aspectRatio == AspectRatio.FIVE_FOUR)
        //{
        //    num = 44f;
        //}
        //Locator.GetPlayerCamera().GetComponent<PlayerCameraController>().SnapToFieldOfView(num, 0.7f, true);
        //if (Locator.GetPlayerSuit().IsWearingSuit(true) && Locator.GetPlayerDetector().GetComponent<OxygenDetector>().GetDetectOxygen())
        //{
        //    Locator.GetPlayerSuit().RemoveHelmet();
        //}
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
    }
}


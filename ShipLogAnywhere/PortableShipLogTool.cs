using System.Collections.Generic;
using UnityEngine;
using OWML.Common;
using static UnityEngine.GridBrushBase;

namespace ShipLogAnywhere;

public class PortableShipLogTool : PlayerTool
{
    private ShipLogController shipLogController;
    private GameObject objectBase;
    private bool InGame => LoadManager.GetCurrentScene() == OWScene.SolarSystem || LoadManager.GetCurrentScene() == OWScene.EyeOfTheUniverse;
    public PortableShipLogTool()
    {

        this._moveSpring = new DampedSpringQuat(15f, 0.8f);
        GameObject shipLogObject = GameObject.Find("ShipLog");
        if (shipLogObject == null)
        {
            ShipLogAnywhere.modHelper.Console.WriteLine("Could not find ship log gobject!", MessageType.Error);
            return;
        }
        else
        {
            shipLogController = shipLogObject.GetComponent<ShipLogController>();
        }
        ShipLogAnywhere.modHelper.Events.Unity.RunWhen(() => GameObject.Find("MainToolRoot") != null, () =>
        {
            Transform toolRoot = GameObject.Find("MainToolRoot")?.transform;
            if (toolRoot != null)
            {
                this._stowTransform = new GameObject("StowTransform").transform;
                this._stowTransform.SetParent(toolRoot, false);

                this._holdTransform = new GameObject("HoldTransform").transform;
                this._holdTransform.SetParent(toolRoot, false);

                Vector3 offsetPos = toolRoot.position + -toolRoot.up * ShipLogAnywhere.gobjectDistanceToCamera;
                Quaternion lookRot = Quaternion.LookRotation(toolRoot.position - offsetPos, toolRoot.forward);
                this._stowTransform.transform.rotation = lookRot;
                this._stowTransform.transform.position = offsetPos;
                offsetPos = toolRoot.position + toolRoot.forward * ShipLogAnywhere.gobjectDistanceToCamera;
                lookRot = Quaternion.LookRotation(toolRoot.position - offsetPos, toolRoot.up);
                this._holdTransform.transform.rotation = lookRot;
                this._holdTransform.transform.position = offsetPos;
                objectBase = this.transform.Find("ShipLogMirrorBase").gameObject;
                ShipLogAnywhere.modHelper.Console.WriteLine("Set up transforms", MessageType.Success);
            }
            else
            {
                ShipLogAnywhere.modHelper.Console.WriteLine("CameraRoot not found!", MessageType.Error);
            }
        });
        GlobalMessenger.AddListener("ExitShipComputer", OnExitShipComputer);
    }
    private void OnExitShipComputer()
    {
        if (this._isEquipped)
        {
            this.UnequipTool();
        }
    }
    public void update()
    {
        bool shouldBeActive = this.IsEquipped() || this.IsPuttingAway();
        if (this.objectBase.activeSelf != shouldBeActive)
        {
            this.objectBase.SetActive(shouldBeActive);
        }
    }

    public override void EquipTool()
    {
        if (!Locator.GetPlayerSuit().IsWearingSuit() && ShipLogAnywhere._requireSuit)
            return;
        if (!shipLogController || !shipLogController.gameObject.activeInHierarchy || shipLogController._damaged)
        {
            NotificationManager.SharedInstance.PostNotification(new NotificationData(NotificationTarget.Player, "Ship Log Unavailable."), false);
            return;
        }
        //this is mostly a re-implentation of the ShipLogController.EnterShipComputer() method.
        Locator.GetToolModeSwapper().UnequipTool();
        base.EquipTool();
        shipLogController.enabled = true;
        shipLogController._usingShipLog = true;
        shipLogController._exiting = false;
        shipLogController._shipLogCanvas.gameObject.SetActive(true);
        shipLogController._canvasAnimator.AnimateTo(1f, Vector3.one * 0.001f, 0.5f, null, false);
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
        //Call picky firevent to exclude player camera
        ShipLogAnywhere.Instance.pickyFireEvent("EnterShipComputer", new List<object> { FindObjectOfType<PlayerCameraController>() });
        shipLogController._splashScreen.OnEnterComputer();
        if (PlayerData.GetDetectiveModeEnabled())
        {
            shipLogController._detectiveMode.OnEnterComputer();
        }
        shipLogController._mapMode.OnEnterComputer();
        shipLogController._currentMode.EnterMode("", list);
    }
}
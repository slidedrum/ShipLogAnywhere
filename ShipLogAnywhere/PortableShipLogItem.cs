using System.Collections.Generic;
using UnityEngine;
using OWML.Common;

namespace ShipLogAnywhere;

public class PortableShipLogItem : OWItem
{
    public static readonly ItemType ItemType = ShipLogAnywhere.Instance.PortableShipLogType;
    private ShipLogController shipLogController;
    public Transform _downTransform;
    public Transform _upTransform;
    public bool _holding = false;
    public PortableShipLogItem()
    {
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
            GameObject toolRoot = GameObject.Find("MainToolRoot");
            Transform cameraRoot = GameObject.Find("MainToolRoot")?.transform;
            if (cameraRoot != null)
            {
                this._downTransform = new GameObject("StowTransform").transform;
                this._downTransform.SetParent(cameraRoot, false);

                this._upTransform = new GameObject("HoldTransform").transform;
                this._upTransform.SetParent(cameraRoot, false);

                Vector3 offsetPos = cameraRoot.position + -cameraRoot.up * ShipLogAnywhere.gobjectDistanceToCamera;
                Quaternion lookRot = Quaternion.LookRotation(cameraRoot.position - offsetPos, cameraRoot.forward);
                this._downTransform.transform.rotation = lookRot;
                this._downTransform.transform.position = offsetPos;
                offsetPos = cameraRoot.position + cameraRoot.forward * ShipLogAnywhere.gobjectDistanceToCamera;
                lookRot = Quaternion.LookRotation(cameraRoot.position - offsetPos, cameraRoot.up);
                this._upTransform.transform.rotation = lookRot;
                this._upTransform.transform.position = offsetPos;
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
    }

    public override void Awake()
    {
        base.Awake();
        _type = ItemType;
    }
    public override void PickUpItem(Transform holdTranform)
    {
        base.PickUpItem(holdTranform);
        transform.localPosition = new Vector3(0f, - 0.2f, 0.1f);
        transform.localRotation = Quaternion.Euler(0f, 240f, 0f);
        transform.localScale = Vector3.one * 0.5f;
        _holding = true;
    }
    public override void DropItem(Vector3 position, Vector3 normal, Transform parent, Sector sector, IItemDropTarget customDropTarget)
    {
        base.DropItem(position, normal, parent, sector, customDropTarget);

        var playerTransform = Locator._playerBody.transform;
        transform.rotation = Quaternion.LookRotation(playerTransform.up, -playerTransform.forward);
        transform.localScale = Vector3.one;
        _holding = false;
    }
    public override string GetDisplayName()
    {
        return "Portable Ship Log";
    }
    public void lookAtLog()
    {
        if (!shipLogController || !shipLogController.gameObject.activeInHierarchy || shipLogController._damaged)
        {
            NotificationManager.SharedInstance.PostNotification(new NotificationData(NotificationTarget.Player, "Ship Log Unavailable."), false);
            return;
        }
        //this is mostly a re-implentation of the ShipLogController.EnterShipComputer() method.
        //Locator.GetToolModeSwapper().UnequipTool();
        //base.EquipTool();
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
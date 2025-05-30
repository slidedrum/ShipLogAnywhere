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
    public bool _looking = false;
    Vector3 baseScale;
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
        baseScale = this.transform.localScale;
        GlobalMessenger.AddListener("ExitShipComputer", OnExitShipComputer);
    }

    private void OnExitShipComputer()
    {
        if (_looking == true)
        {
            this._looking = false;
            this.transform.localPosition = new Vector3(0f, -0.2f, 0.1f);
            this.transform.localRotation = Quaternion.Euler(0f, 240f, 0f);
            this.transform.localScale = baseScale * 0.5f;
        }
    }

    public override void Awake()
    {
        base.Awake();
        _type = ItemType;
    }
    public override void PickUpItem(Transform holdTranform)
    {
        base.PickUpItem(holdTranform);
        this.transform.localPosition = new Vector3(0f, - 0.2f, 0.1f);
        this.transform.localRotation = Quaternion.Euler(0f, 240f, 0f);
        this.transform.localScale = baseScale * 0.5f;
        this._holding = true;
    }
    public override void DropItem(Vector3 position, Vector3 normal, Transform parent, Sector sector, IItemDropTarget customDropTarget)
    {
        base.DropItem(position, normal, parent, sector, customDropTarget);

        var playerTransform = Locator._playerBody.transform;
        this.transform.rotation = Quaternion.LookRotation(playerTransform.up, -playerTransform.forward);
        this.transform.localScale = baseScale;
        this._holding = false;
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
        if (_looking == true)
        {
            return;
        }
        _looking = true;


        Transform cameraTransform = Locator.GetPlayerCamera().transform;
        Vector3 worldTargetPosition = cameraTransform.position + cameraTransform.forward * 0.5f;
        this.transform.localPosition = this.transform.parent.InverseTransformPoint(worldTargetPosition);
        Quaternion worldLookRotation = Quaternion.LookRotation(cameraTransform.position - worldTargetPosition, cameraTransform.up);
        Quaternion localLookRotation = Quaternion.Inverse(this.transform.parent.rotation) * worldLookRotation;
        this.transform.localRotation = localLookRotation;
        this.transform.localScale = baseScale;


        //this is mostly a re-implentation of the ShipLogController.EnterShipComputer() method.
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
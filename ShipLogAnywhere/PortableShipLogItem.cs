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
    DampedSpringQuat _moveSpring = new DampedSpringQuat(15f, 0.8f);
    DampedSpring3D _positionSpring = new DampedSpring3D(15f, 0.8f);
    DampedSpring3D _scaleSpring = new DampedSpring3D(15f, 0.8f);
    private Vector3 targetPosition;
    private Vector3 targetScale;
    public PortableShipLogItem()
    {

    }

    private void OnExitShipComputer()
    {
        if (this._looking == true)
        {
            this._looking = false;
        }
    }

    public override void Awake()
    {
        base.Awake();
        _type = ItemType;
        GameObject shipLogObject = GameObject.Find("ShipLog");
        if (shipLogObject == null)
        {
            ShipLogAnywhere.modHelper.Console.WriteLine("Could not find ship log gobject!", MessageType.Error);
            return;
        }
        shipLogController = shipLogObject.GetComponent<ShipLogController>();
        setUpTransforms();
        GlobalMessenger.AddListener("ExitShipComputer", OnExitShipComputer);
    }
    public void setUpTransforms()
    {
        if (_upTransform == null)
        {
            GameObject upGO = new GameObject("UpTransform");
            upGO.transform.SetParent(this.transform, false);
            _upTransform = upGO.transform;
        }

        if (_downTransform == null)
        {
            GameObject downGO = new GameObject("DownTransform");
            downGO.transform.SetParent(this.transform, false);
            _downTransform = downGO.transform;
        }
        if (baseScale == Vector3.zero)
            baseScale = this.transform.localScale;

        Transform cameraTransform = Locator.GetPlayerCamera().transform;
        Vector3 worldTargetPosition = cameraTransform.position + cameraTransform.forward * 0.5f;
        this._upTransform.localPosition = this.transform.parent.InverseTransformPoint(worldTargetPosition);
        Quaternion worldLookRotation = Quaternion.LookRotation(cameraTransform.position - worldTargetPosition, cameraTransform.up);
        Quaternion localLookRotation = Quaternion.Inverse(this.transform.parent.rotation) * worldLookRotation;
        this._upTransform.localRotation = localLookRotation;
        this._upTransform.localScale = baseScale;

        // Set down transform as before
        this._downTransform.localPosition = new Vector3(0f, -0.2f, 0.1f);
        this._downTransform.localRotation = Quaternion.Euler(0f, 240f, 0f);
        this._downTransform.localScale = this.baseScale * 0.5f;

        ShipLogAnywhere.modHelper.Console.WriteLine("Set up transforms for item", MessageType.Success);
    }

    public void Update()
    {
        if (_holding)
        {
            Quaternion quaternion = (this._looking ? this._upTransform.localRotation : this._downTransform.localRotation);
            base.transform.localRotation = this._moveSpring.Update(base.transform.localRotation, quaternion, Time.deltaTime);
            targetPosition = this._looking ? this._upTransform.localPosition : this._downTransform.localPosition;
            this.transform.localPosition = _positionSpring.Update(this.transform.localPosition, targetPosition, Time.deltaTime);
            targetScale = this._looking ? this._upTransform.localScale : this._downTransform.localScale;
            this.transform.localScale = _scaleSpring.Update(this.transform.localScale, targetScale, Time.deltaTime);
        }
    }
    public override void PickUpItem(Transform holdTranform)
    {
        
        base.PickUpItem(holdTranform);
        this._looking = false;
        this._holding = true;
        this.transform.localScale = this.baseScale * 0.2f;
    }
    public override void DropItem(Vector3 position, Vector3 normal, Transform parent, Sector sector, IItemDropTarget customDropTarget)
    {
        base.DropItem(position, normal, parent, sector, customDropTarget);

        var playerTransform = Locator._playerBody.transform;
        this.transform.rotation = Quaternion.LookRotation(playerTransform.up, -playerTransform.forward);
        this.transform.localScale = this.baseScale * 0.3f;
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
        if (this._looking == true)
        {
            return;
        }
        this._looking = true;

        setUpTransforms();  //I don't understand why this needs to be here.  It probably doesn't but it works and I'm tired of messing with it.

        //Transform cameraTransform = Locator.GetPlayerCamera().transform;
        //Vector3 worldTargetPosition = cameraTransform.position + cameraTransform.forward * 0.5f;
        //this._upTransform.localPosition = this.transform.parent.InverseTransformPoint(worldTargetPosition);
        //Quaternion worldLookRotation = Quaternion.LookRotation(cameraTransform.position - worldTargetPosition, cameraTransform.up);
        //Quaternion localLookRotation = Quaternion.Inverse(this.transform.parent.rotation) * worldLookRotation;
        //this._upTransform.localRotation = localLookRotation;
        //this._upTransform.localScale = baseScale;


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
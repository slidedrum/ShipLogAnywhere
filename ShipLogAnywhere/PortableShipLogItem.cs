using System.Collections.Generic;
using System;
using UnityEngine;
using OWML.ModHelper;
using OWML.Common;
using Epic.OnlineServices;
using System.Linq;

namespace ShipLogAnywhere;

public class PortableShipLogItem : OWItem
{
    public static readonly ItemType ItemType = ShipLogAnywhere.Instance.PortableShipLogType;
    public PortableShipLogItem()
    {

    }
    public override void Awake()
    {
        base.Awake();
        _type = ItemType;
    }

    public override string GetDisplayName()
    {
        return "Portable Ship Log";
    }
}
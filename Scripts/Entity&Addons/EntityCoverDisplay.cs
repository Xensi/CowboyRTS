using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EntityCoverDisplay : EntityAddon
{
    public void CoverDisplayUpdate()
    {
        List<Entity> nearbyCover = Global.instance.spatialHash.GetNearbyCover(ent);
        //string s = "";
        foreach (var item in nearbyCover)
        {
            Debug.DrawRay(item.transform.position, Vector3.up, Color.green);
            //s += item.name + " ";
        }
        //if (s != "") Debug.Log(s);
    }
}

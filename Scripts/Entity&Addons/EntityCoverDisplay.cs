using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EntityCoverDisplay : EntityAddon
{
    [SerializeField] List<CoverSlice> coverSlices = new();

    //Indicate towards nearby cover
    public void CoverDisplayUpdate()
    {
        List<Entity> nearbyCover = Global.instance.spatialHash.GetNearbyCover(ent);
        //string s = "";
        int i = 0;
        foreach (var item in nearbyCover)
        {
            Debug.DrawRay(item.transform.position, Vector3.up, Color.green);
            //s += item.name + " ";
            coverSlices[i].UpdateCoverToFollow(item);
            i++;
        }
        for (; i < coverSlices.Count; i++) //remove remaining
        {
            coverSlices[i].UpdateCoverToFollow(null);
        }
        //if (s != "") Debug.Log(s);
    }
}

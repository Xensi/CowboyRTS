using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class LootOnDestruction : MonoBehaviour
{
    [SerializeField] private int goldToLootOnDestruction = 0;

    public void LootForLocalPlayer()
    { 
        Global.Instance.localPlayer.AddGold(goldToLootOnDestruction);
    }
}

using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
public class Player : NetworkBehaviour
{
    public Faction playerFaction;
    public List<SelectableEntity> ownedEntities;
    public int gold = 100;
    public int population = 0;
    public int maxPopulation = 10;

    public virtual void Start()
    {
        if (playerFaction != null)
        {
            Debug.Log("Setting gold and pop");
            gold = playerFaction.startingGold;
            maxPopulation = playerFaction.startingMaxPopulation;
        }
    } 
}

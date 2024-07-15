using FoW;
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
    public int teamID = 0;

    public virtual void Start()
    {
        if (playerFaction != null)
        {
            Debug.Log("Setting gold and pop");
            gold = playerFaction.startingGold;
            maxPopulation = playerFaction.startingMaxPopulation;
        }
    }
    public bool CheckIfPositionIsOnRamp(Vector3 position)
    {
        int use;
        //first check if hit position is very close to integer
        int checkAgainst = Mathf.RoundToInt(position.y);
        if (Mathf.Abs(checkAgainst - position.y) < 0.01f) //
        {
            use = checkAgainst;
        }
        else
        {
            use = Mathf.CeilToInt(position.y);
        }

        float buffer = 0.5f;
        Vector3 testPosition = new Vector3(position.x, use + buffer, position.z);
        if (Physics.Raycast(testPosition, Vector3.down, out RaycastHit rampHit, Mathf.Infinity, Global.Instance.groundLayer))
        {
            float distance = Mathf.Abs(testPosition.y - rampHit.point.y);
            return distance > 0.01f + buffer || distance < buffer - 0.01f;
        }
        return false;
    }

    public bool IsPositionBlocked(Vector3 position)
    {
        bool onRamp = CheckIfPositionIsOnRamp(position);
        bool placementBlocked = false;

        FogOfWarTeam fow = FogOfWarTeam.GetTeam((int)teamID);
        if (fow.GetFogValue(position) > 0.1f * 255)
        {
            placementBlocked = true;
        }
        else
        {
            if (onRamp)
            {
                placementBlocked = true;
            }
            else
            {
                float sides = .24f;
                float height = .5f;
                Vector3 halfExtents = new Vector3(sides, height, sides);
                Vector3 center = new Vector3(position.x, position.y + height, position.z);
                placementBlocked = Physics.CheckBox(center, halfExtents, Quaternion.identity, 
                    Global.Instance.blockingLayer, QueryTriggerInteraction.Ignore);
            }
        }
        return placementBlocked;
    }
}

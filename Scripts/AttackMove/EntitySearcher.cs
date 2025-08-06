using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Threading.Tasks;
using Unity.Netcode;
using UtilityMethods;
/// <summary>
/// Used to asynchronously put enemies into arrays so units can attack them.
/// </summary>
public class EntitySearcher : MonoBehaviour
{
    [SerializeField] private List<StateMachineController> assignedUnits = new();
    private float timer = 0;
    private float searchTime = 0.1f;
    [SerializeField] private float searchRadius = 4;
    public int structureCount = 0;
    public Entity[] searchedStructures = new Entity[0];
    public Entity[] searchedMinions = new Entity[0];
    public Entity[] searchedAll = new Entity[0];
    public int allCount = 0;
    public int minionCount = 0;
    public int creatorAllegianceID = 0; //by default 0 is player, 1 is AI
    public DisplayRadius dr;
    public DisplayRadius defaultDR;
    public CrosshairDisplay crosshairPrefab;
    private bool visible = true;
    private bool searchingInProgress = false;
    int tempAllCount = 0;
    int tempMinionCount = 0;
    int tempStructureCount = 0;
    [SerializeField] private List<CrosshairDisplay> crosshairs = new();
    int neededCrosshairs = 0;
    public Player playerCreator;
    private void Start()
    { 
        searchedStructures = new Entity[Global.instance.attackMoveDestinationEnemyArrayBufferSize];
        searchedMinions = new Entity[Global.instance.attackMoveDestinationEnemyArrayBufferSize];
        searchedAll = new Entity[Global.instance.fullEnemyArraySize];
        SearchHash();
        if (defaultDR != null)
        {
            defaultDR.SetLREnable(false);
        }
        CheckIfShouldBeVisible();
    }
    void Update()
    {
        //when timer elapses perform a search
        timer += Time.deltaTime;
        if (timer >= searchTime)
        {
            if (!searchingInProgress)
            {
                timer = 0;
                SearchHash();
            }
        }
        CleanAssignedUnits();
        CheckIfShouldBeVisible();
        HighlightRelevantEnemies();
    }
    private void CleanAssignedUnits()
    {
        for (int i = assignedUnits.Count - 1; i >= 0; i--)
        {
            StateMachineController item = assignedUnits[i];
            if (item == null || !item.ent.alive) assignedUnits.Remove(item);
        }
    }
    private void UpdateVisibility()
    {
        if (dr != null) dr.SetLREnable(visible);
    }
    public bool MinionsInSearch()
    {
        return tempMinionCount > 0 || minionCount > 0;
    }
    private bool CheckIfShouldBeVisible()
    {
        bool atLeastOneUnitSelected = false;
        foreach (StateMachineController item in assignedUnits)
        {
            if (item != null && item.ent != null && item.ent.selected) atLeastOneUnitSelected = true;
            break;
        }
        visible = atLeastOneUnitSelected;
        if (!visible)
        {
            if (assignedUnits.Count <= 0)
            {
                Destroy(gameObject);
            }
        }
        UpdateVisibility();
        return atLeastOneUnitSelected;
    }
    /// <summary>
    /// Returns search radius.
    /// </summary>
    /// <returns></returns>
    public float SearchRadius()
    {
        return searchRadius;
    }
    /// <summary>
    /// Query the Spatial Hash system to populate the search arrays.
    /// </summary>
    private void SearchHash()
    {
        Global.instance.spatialHash.EntitySearchHash(transform.position, searchRadius, playerCreator,
            ref searchedMinions, ref searchedStructures, ref searchedAll,
            ref minionCount, ref structureCount, ref allCount);
    }
    public void AssignUnit(StateMachineController unit)
    {
        assignedUnits.Add(unit);
    }
    public void UnassignUnit(StateMachineController unit)
    {
        assignedUnits.Remove(unit);
        DeleteIfNoAssignedUnits();
    }
    private void OnDestroy()
    {
        ClearCrosshairs();
    }
    private void ClearCrosshairs()
    {
        foreach (CrosshairDisplay crosshair in crosshairs)
        {
            if (crosshair != null) Destroy(crosshair.gameObject);
        }
    }
    public void DeleteIfNoAssignedUnits()
    {
        if (assignedUnits.Count <= 0)
        {
            //Debug.Log("Deleting bc no assigned units");
            Destroy(gameObject);
        }
    }
    private void OnDrawGizmos()
    {
        Gizmos.DrawWireSphere(transform.position, searchRadius);
    }
    private void CreateNeededCrosshairs(int neededCrosshairs)
    {
        //if we lack crosshairs, create some
        while (crosshairs.Count < neededCrosshairs)
        {
            CrosshairDisplay cd = Instantiate(crosshairPrefab, Vector3.zero, Quaternion.identity);
            cd.AssignEntitySearcher(this);
            crosshairs.Add(cd);
        }
    }
    private void HighlightRelevantEnemies()
    {
        bool checkMinions = true;
        if (tempMinionCount > 0 || minionCount > 0)
        {
            neededCrosshairs = minionCount > tempMinionCount ? minionCount : tempMinionCount;
            checkMinions = true;
        }
        else if (tempStructureCount > 0 || structureCount > 0)
        {
            neededCrosshairs = structureCount > tempStructureCount ? structureCount : tempStructureCount;
            checkMinions = false;
        }
        else
        {
            neededCrosshairs = 0;
        }
        if (defaultDR != null)
        {
            defaultDR.SetLREnable(neededCrosshairs <= 0 && visible);
        }
        CreateNeededCrosshairs(neededCrosshairs);
        if (visible)
        {
            int numberOfCrosshairsUsed = 0;
            bool allCrosshairsUsed = false;
            for (int i = crosshairs.Count - 1; i >= 0; i--)
            {
                if (crosshairs[i] == null)
                {
                    crosshairs.RemoveAt(i);
                    continue;
                }
                crosshairs[i].SetPulse(false);

                if (!allCrosshairsUsed)
                {
                    Entity ent = null;
                    if (checkMinions)
                    {
                        ent = searchedMinions[i];
                    }
                    else
                    {
                        ent = searchedStructures[i];
                    }
                    if (ent == null)
                    {
                        crosshairs[i].SetEntitySearcherVisible(false);
                        crosshairs[i].assignedEntity = null;
                    }
                    else
                    {
                        crosshairs[i].SetEntitySearcherVisible(true);
                        crosshairs[i].transform.SetParent(ent.transform, false);
                        crosshairs[i].assignedEntity = ent;
                        ent.entitySearcherCrosshairTargetingThis = crosshairs[i]; //assign the crosshair to the entity
                                                                                  //check if any assigned units are attacking the enemy the crosshair is assigned to
                        foreach (StateMachineController item in assignedUnits)
                        {
                            if (!Exists(item)) continue;
                            if (item.attacker != null && item.attacker.targetEnemy == ent)
                            {
                                crosshairs[i].SetPulse(true);
                                break;
                            }
                        }
                        numberOfCrosshairsUsed++;
                    }
                }
                else //disable
                {
                    crosshairs[i].SetEntitySearcherVisible(false);
                    //crosshairs[i].UpdateVisibility(false);
                    crosshairs[i].assignedEntity = null;
                }
                if (numberOfCrosshairsUsed >= neededCrosshairs) allCrosshairsUsed = true;
            }
        }
        else
        {
            for (int i = 0; i < crosshairs.Count; i++)
            {
                if (crosshairs[i] == null) continue;
                crosshairs[i].SetEntitySearcherVisible(false);
                //crosshairs[i].UpdateVisibility(false);
                crosshairs[i].assignedEntity = null;
                crosshairs[i].SetPulse(false);
            }
        }
        
    }

    /// <summary>
    /// Does this exist or is it in the process of being deleted?
    /// </summary>
    /// <param name="target"></param>
    /// <returns></returns>
    public bool Exists(StateMachineController target)
    {
        if (target == null || target.ent == null || !target.ent.alive || target.ent.currentHP.Value <= 0)
        {
            return false;
        }
        else
        {
            return true;
        }
    }
}

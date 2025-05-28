using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Threading.Tasks;
/// <summary>
/// Used to asynchronously put enemies into arrays so units can attack them.
/// </summary>
public class EntitySearcher : MonoBehaviour
{
    [SerializeField] private List<StateMachineController> assignedUnits = new();
    private float timer = 0;
    private float searchTime = 0.1f; 
    private int searchedCount = 0; //up to where in array search results are valid
    [SerializeField] private float searchRadius = 4;
    public int structureCount = 0;
    public SelectableEntity[] searchedStructures = new SelectableEntity[0];
    public SelectableEntity[] searchedMinions = new SelectableEntity[0];
    public SelectableEntity[] searchedAll = new SelectableEntity[0];
    public int allCount = 0;
    public int minionCount = 0;
    public int creatorAllegianceID = 0; //by default 0 is player, 1 is AI
    public DisplayRadius dr;
    public CrosshairDisplay crosshairPrefab;
    private void Start()
    { 
        searchedStructures = new SelectableEntity[Global.Instance.attackMoveDestinationEnemyArrayBufferSize];
        searchedMinions = new SelectableEntity[Global.Instance.attackMoveDestinationEnemyArrayBufferSize];
        searchedAll = new SelectableEntity[Global.Instance.fullEnemyArraySize];
        Search();
    }
    void Update()
    {
        //when timer elapses perform a search
        timer += Time.deltaTime;
        if (timer >= searchTime)
        {
            timer = 0;
            Search();
            HighlightRelevantEnemies();
        }
        DeleteIfNoAssignedUnits();
        if (dr != null)
        {
            //Debug.Log("TEST");
            dr.UpdateLR();
        }
    }
    public float SearchRadius()
    {
        return searchRadius;
    }
    private async void Search()
    {
        //create a list of viable targets to attack   
        Collider[] enemyArray = new Collider[Global.Instance.fullEnemyArraySize];
        LayerMask searchMask;
        if (creatorAllegianceID == 0)
        {
            searchMask = Global.Instance.enemyLayer;
        }
        else
        {
            searchMask = Global.Instance.friendlyEntityLayer;
        }
        searchedCount = Physics.OverlapSphereNonAlloc(transform.position, searchRadius, enemyArray, searchMask); //use fixed distance for now
        int tempMinionCount = 0;
        int tempStructureCount = 0;
        int tempAllCount = 0;
        for (int i = 0; i < searchedCount; i++) //place valid entities into array
        {
            if (enemyArray[i] == null) continue; //if invalid do not increment slotToWriteTo
            SelectableEntity select = enemyArray[i].GetComponent<SelectableEntity>();
            
            
            if (select == null) continue;
            if (!select.alive || !select.isTargetable.Value || !select.isAttackable) //overwrite these slots
            {
                continue;
            } 

            if (select.IsMinion())
            {
                if (tempMinionCount < searchedMinions.Length)
                { 
                    searchedMinions[tempMinionCount] = select;
                    tempMinionCount++;
                }
            }
            else
            {
                if (tempStructureCount < searchedStructures.Length)
                { 
                    searchedStructures[tempStructureCount] = select;
                    tempStructureCount++;
                }
            }
            if (tempAllCount < searchedAll.Length)
            { 
                searchedAll[tempAllCount] = select;
                tempAllCount++;
            }
            await Task.Yield();
        }
        allCount = tempAllCount;
        minionCount = tempMinionCount;
        structureCount = tempStructureCount;
    }
    public void AssignUnit(StateMachineController unit)
    {
        assignedUnits.Add(unit);
    }
    public void UnassignUnit(StateMachineController unit)
    {
        assignedUnits.Remove(unit);
    }
    public void DeleteIfNoAssignedUnits()
    {
        if (assignedUnits.Count <= 0)
        {
            Destroy(gameObject);
        }
    }
    private void OnDrawGizmos()
    {
        Gizmos.DrawWireSphere(transform.position, searchRadius);
    }
    private List<CrosshairDisplay> crosshairs = new();
    int neededCrosshairs = 0;
    private void HighlightRelevantEnemies()
    {
        bool checkMinions = true;
        if (minionCount > 0)
        {
            neededCrosshairs = minionCount;
            checkMinions = true;
        }
        else if (structureCount > 0)
        {
            neededCrosshairs = structureCount;
            checkMinions = false;
        }
        //if we lack crosshairs, create some
        while (crosshairs.Count < neededCrosshairs)
        {
            CrosshairDisplay cd = Instantiate(crosshairPrefab, Vector3.zero, Quaternion.identity);
            crosshairs.Add(cd);
        }
        
        for (int i = 0; i < crosshairs.Count; i++)
        {
            if (i < neededCrosshairs)
            {
                SelectableEntity ent = null;
                if (checkMinions)
                {
                    ent = searchedMinions[i];
                }
                else
                {
                    ent = searchedStructures[i];
                }
                crosshairs[i].transform.SetParent(ent.transform, false);
                crosshairs[i].UpdateVisibility(true);
            }
            else //disable
            {
                crosshairs[i].UpdateVisibility(false);
            }
        }
    }
}

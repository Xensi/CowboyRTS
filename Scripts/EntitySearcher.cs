using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Threading.Tasks;

public class EntitySearcher : MonoBehaviour
{
    [SerializeField] private List<MinionController> assignedUnits = new();
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

    private void Start()
    { 
        searchedStructures = new SelectableEntity[Global.Instance.attackMoveDestinationEnemyArrayBufferSize];
        searchedMinions = new SelectableEntity[Global.Instance.attackMoveDestinationEnemyArrayBufferSize];
        searchedAll = new SelectableEntity[Global.Instance.attackMoveDestinationEnemyArrayBufferSize];
        Search();
    }
    void Update()
    {
        //update entity searcher timer

        //when timer elapses perform a search
        timer += Time.deltaTime;
        if (timer >= searchTime)
        {
            timer = 0;
            Search();
        }
        DeleteIfNoAssignedUnits();
    }
    private void Search()
    {
        //create a list of viable targets to attack   
        Collider[] enemyArray = new Collider[Global.Instance.attackMoveDestinationEnemyArrayBufferSize];
        searchedCount = Physics.OverlapSphereNonAlloc(transform.position, searchRadius, enemyArray, Global.Instance.enemyLayer); //use fixed distance for now
        minionCount = 0;
        structureCount = 0;
        allCount = 0;
        for (int i = 0; i < searchedCount; i++) //place valid entities into array
        {
            if (enemyArray[i] == null) continue; //if invalid do not increment slotToWriteTo
            SelectableEntity select = enemyArray[i].GetComponent<SelectableEntity>();
            if (select == null) continue;
            if (!select.alive || !select.isTargetable.Value) //overwrite these slots
            {
                continue;
            } 

            if (select.IsMinion())
            {
                searchedMinions[minionCount] = select;
                minionCount++; 
            }
            else
            {
                searchedStructures[structureCount] = select;
                structureCount++; 
            }
            searchedAll[allCount] = select;
            allCount++;
        }
    }
    public void AssignUnit(MinionController unit)
    {
        assignedUnits.Add(unit);
    }
    public void UnassignUnit(MinionController unit)
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
}

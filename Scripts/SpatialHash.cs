using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UtilityMethods;
using UnityEngine.Profiling;
using static Attacker;
using System;
public class SpatialHash : MonoBehaviour
{
    private const int hashX = 73856093;
    private const int hashY = 19349663;

    Global global;
    private int tableSize;
    [SerializeField] private int[] table;
    [SerializeField] private Entity[] denseEntityArray;
    private float spacing = 1;
    private void Start()
    {
        global = Global.instance;
        tableSize = global.GetMaxEntities();
        table = new int[tableSize];
        denseEntityArray = new Entity[tableSize];
    }
    private void Update()
    {
        ParseParticles();
    }

    private int HashCoordinates(int xi, int yi)
    {
        int h = (xi * hashX) ^ (yi * hashY); //^ is XOR
        return Mathf.Abs(h) % tableSize;
    }
    private int IntCoord(float f)
    {
        float offset = 0.5f;
        return Mathf.FloorToInt(f + offset / spacing);
    }
    private void ParseParticles() //minute physics solution
    {
        Profiler.BeginSample("Parsing particles");
        int[] tempTable = new int[tableSize];
        Entity[] tempEntityArray = new Entity[global.GetNumEntities()];
        foreach (Entity ent in global.GetEntityList())
        {
            int x = IntCoord(ent.transform.position.x);
            int y = IntCoord(ent.transform.position.z);
            int h = HashCoordinates(x, y); //compute hash value of the cell
            ent.SetHash(h);
            tempTable[h]++; //this shows us how many particles are in each cell using hash coords
        }
        int lastVal = 0;
        for (int i = 0; i < tableSize; i++) //run through this array and compute partial sums
        {
            tempTable[i] += lastVal;
            lastVal = tempTable[i];
        }
        //now the hash table has the last cell entry + 1 instead of the first cell entry
        //fill in the particle array
        foreach (Entity ent in global.GetEntityList())
        {
            int tableIndex = ent.GetHash();
            tempTable[tableIndex]--; //decrease it by one
            int particleIndex = tempTable[tableIndex];
            tempEntityArray[particleIndex] = ent; //assign to particle array
        }
        denseEntityArray = tempEntityArray;
        table = tempTable;
        Profiler.EndSample();
    }
    private int[] GetHashesToCheck(Vector3 pos, float rangeRadius)
    {
        int startX = IntCoord(pos.x);
        int startY = IntCoord(pos.z);
        //get surrounding cells and calculate hash coordinates
        int maxDist = Mathf.CeilToInt(rangeRadius);
        int x0 = IntCoord(startX - maxDist);
        int x1 = IntCoord(startX + maxDist + 1);
        int y0 = IntCoord(startY - maxDist);
        int y1 = IntCoord(startY + maxDist + 1);
        int width = Mathf.Abs(x0 - x1);
        int height = Mathf.Abs(y0 - y1);
        int numHashes = width * height;
        int[] hashesToCheck = new int[numHashes]; //if dist is 1, then 9 spaces needed;
        //build set of cells to check
        int hashIndex = 0;
        for (int xi = x0; xi < x1; xi++)
        {
            for (int yi = y0; yi < y1; yi++)
            {
                //Debug.DrawRay(new Vector3(xi, 0, yi), Vector3.up, Color.white);
                int h = HashCoordinates(xi, yi); //remember that this can result in hash collisions
                hashesToCheck[hashIndex] = h;
                hashIndex++;
            }
        }
        return hashesToCheck;
    }
    public Entity[] GetEntitiesInRange(Vector3 pos, Entity queryingEntity, float rangeRadius)
    {
        int[] hashesToCheck = GetHashesToCheck(pos, rangeRadius);
        List<Entity> tempList = new();
        //check through cells
        foreach (int h in hashesToCheck)
        {
            int denseStart = table[h]; //start of dense
            int denseEnd = table[Mathf.Clamp(h + 1, 0, tableSize - 1)]; //check where the next cell starts to get the number of particles to check in the dense array

            for (int i = denseStart; i <= denseEnd; i++)
            {
                int clampedIndex = Mathf.Clamp(i, 0, global.GetNumEntities() - 1);
                Entity targetEnt = denseEntityArray[clampedIndex];
                if (targetEnt == null) continue;
                if (targetEnt == queryingEntity) continue;

                float combined = targetEnt.GetRadius() + rangeRadius;
                bool inRange = Util.FastDistanceCheck(pos, targetEnt.transform.position, combined);
                if (inRange) // square the distance we compare with
                {
                    if (!tempList.Contains(targetEnt)) tempList.Add(targetEnt);
                }
            }
        }
        return tempList.ToArray();
    }
    private int GetDenseStart(int h)
    {
        return table[h];
    }
    private int GetDenseEnd(int h)
    {
        return table[Mathf.Clamp(h + 1, 0, tableSize - 1)];
    }
    private int GetIndexClampedByNumEntities(int i)
    {
        return Mathf.Clamp(i, 0, global.GetNumEntities() - 1);
    }
    private float GetCombinedRadii(Entity target, float range)
    {
        return target.GetRadius() + range;
    }
    public void EntitySearchHash(Vector3 pos, float rangeRadius, Player player,
        ref Entity[] searchedMinions, ref Entity[] searchedStructures, ref Entity[] searchedAll,
        ref int minionCount, ref int structureCount, ref int allCount)
    {
        int tempAllCount = 0;
        int tempMinionCount = 0;
        int tempStructureCount = 0;
        List<Entity> stashedEntities = new();
        Array.Clear(searchedMinions, 0, searchedMinions.Length);
        Array.Clear(searchedStructures, 0, searchedStructures.Length);
        Array.Clear(searchedAll, 0, searchedAll.Length);
        foreach (int h in GetHashesToCheck(pos, rangeRadius)) //check through cells
        {
            for (int i = GetDenseStart(h); i <= GetDenseEnd(h); i++)
            {
                Entity targetEnt = denseEntityArray[GetIndexClampedByNumEntities(i)]; //get target
                if (!player.IsValidTarget(targetEnt)) continue; //skip invalid targets
                bool inRange = Util.FastDistanceCheck(pos, targetEnt.transform.position, GetCombinedRadii(targetEnt, rangeRadius));
                if (!inRange) continue; //we filter out all options that are out of range
                if (stashedEntities.Contains(targetEnt)) continue; //don't store targets we've already seen
                stashedEntities.Add(targetEnt);
                if (targetEnt.IsMinion() && tempMinionCount < searchedMinions.Length)
                {
                    searchedMinions[tempMinionCount] = targetEnt;
                    tempMinionCount++;
                }
                else if (tempStructureCount < searchedStructures.Length)
                {
                    searchedStructures[tempStructureCount] = targetEnt;
                    tempStructureCount++;
                }
                if (tempAllCount < searchedAll.Length)
                {
                    searchedAll[tempAllCount] = targetEnt;
                    tempAllCount++;
                }
            }
        }
        allCount = tempAllCount;
        minionCount = tempMinionCount;
        structureCount = tempStructureCount;
    }
    public Entity GetFirstEnemyHashSearch(Entity queryingEntity, float rangeRadius, RequiredEnemyType requiredEnemyType)
    {
        Vector3 pos = queryingEntity.transform.position;
        Entity valid = null;
        Entity backup = null;
        foreach (int h in GetHashesToCheck(pos, rangeRadius)) //check through cells
        {
            for (int i = GetDenseStart(h); i <= GetDenseEnd(h); i++)
            {
                Entity targetEnt = denseEntityArray[GetIndexClampedByNumEntities(i)];
                if (targetEnt == null || targetEnt == queryingEntity) continue;
                if (queryingEntity.attacker != null && !queryingEntity.attacker.IsValidTarget(targetEnt)) continue;
                bool inRange = Util.FastDistanceCheck(pos, targetEnt.transform.position, GetCombinedRadii(targetEnt, rangeRadius));
                if (!inRange) continue; //we filter out all options that are out of range
                switch (requiredEnemyType)
                {
                    case RequiredEnemyType.Any:
                        valid = targetEnt;
                        break;
                    case RequiredEnemyType.Minion:
                        if (targetEnt.IsMinion()) valid = targetEnt;
                        break;
                    case RequiredEnemyType.Structure:
                        if (targetEnt.IsStructure()) valid = targetEnt;
                        break;
                    case RequiredEnemyType.MinionPreferred:
                        if (targetEnt.IsMinion()) //if it's a structure, we'll continue, but leave this open as an option if we can't find any minions
                        {
                            valid = targetEnt;
                        }
                        else
                        {
                            backup = targetEnt;
                        }
                        break;
                }
                if (valid != null) break;
            }
        }
        if (backup != null && valid == null) valid = backup; //if we search all and minion preferred, we can target structure
        return valid;
    }
    public Entity GetClosestMinionHashSearch(Entity queryingEntity, float rangeRadius)
    {
        Vector3 pos = queryingEntity.transform.position;
        Entity closest = null;
        float closestSqrDist = Mathf.Infinity;
        foreach (int h in GetHashesToCheck(pos, rangeRadius)) //check through cells
        {
            for (int i = GetDenseStart(h); i <= GetDenseEnd(h); i++)
            {
                Entity targetEnt = denseEntityArray[GetIndexClampedByNumEntities(i)];
                if (targetEnt == null || targetEnt == queryingEntity) continue;
                if (queryingEntity.attacker != null && !queryingEntity.attacker.IsValidTarget(targetEnt)) continue;
                if (!targetEnt.IsMinion()) continue;
                float newSqrDist = Util.GetSqrDist(pos, targetEnt.transform.position);
                bool closer = Util.SqrDistCheck(newSqrDist, closestSqrDist);
                if (closer)
                {
                    closest = targetEnt;
                    closestSqrDist = newSqrDist;
                }
            }
        }
        if (closest != null)
        {
            bool inRange = Util.FastDistanceCheck(pos, closest.transform.position, GetCombinedRadii(closest, rangeRadius));
            return inRange ? closest : null;
        }
        return null;
    }
}

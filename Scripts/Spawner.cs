using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static StateMachineController;
using static Entity;

public class Spawner : EntityAddon
{
    private GameObject rallyVisual;
    [SerializeField] private SpawnableOptions spawnableOptions;

    List<FactionUnit> spawnables = new();

    [HideInInspector] public List<FactionUnit> buildQueue;
    [HideInInspector] public bool productionBlocked = false;
    [HideInInspector] public Vector3 rallyPoint;
    [HideInInspector] public Entity rallyTarget;
    [HideInInspector] public RallyMission spawnerRallyMission;

    public override void InitAddon()
    {
        if (spawnableOptions != null)
        {
            spawnables = spawnableOptions.spawnables.ToList();
        }

        rallyVisual = Instantiate(Global.instance.rallyPrefab, transform.position, Quaternion.identity);
        rallyPoint = transform.position;
        if (rallyVisual != null) rallyVisual.SetActive(false);
    }
    public Player GetController()
    {
        return ent.playerControllingThis;
    }
    private EntityHealthBar GetProductionBar()
    {
        return ent.productionProgressBar;
    }
    private LineRenderer GetLineIndicator()
    {
        return ent.lineIndicator;
    }
    public void UpdateSpawnables(SpawnableOptions options)
    {
        foreach (FactionUnit item in options.spawnables)
        {
            if (!spawnables.Contains(item)) spawnables.Add(item);
        }
    }
    public List<FactionUnit> GetSpawnables()
    {
        return spawnables;
    }
    private Transform GetSpawnPosition()
    {
        return ent.positionToSpawnMinions;
    }
    private bool DislodgeBlocker(Entity target)
    {
        bool blocked = false;
        if (target != null && target.IsMinion() && target.sm.givenMission != RallyMission.Move
            && target.playerControllingThis == GetController() && target.sm.InState(EntityStates.Idle))
        {
            //tell blocker to get out of the way.
            float randRadius = 1;
            Vector2 randCircle = UnityEngine.Random.insideUnitCircle * randRadius;
            Vector3 rand = target.transform.position + new Vector3(randCircle.x, 0, randCircle.y);
            target.pf.MoveTo(rand);
            //Debug.Log("trying to move blocking unit to: " + rand);
            blocked = true;
        }
        return blocked;
    }
    public void UpdateBuildQueue()
    {
        if (!IsOwner) return;
        if (buildQueue.Count > 0)
        {
            // todo add ability to build multiple from one structure
            FactionUnit fac = buildQueue[0];

            fac.spawnTimer += Time.deltaTime;
            //check if the position is blocked;
            int num = GetController().maxPopulation - GetController().population;
            productionBlocked = true;
            if (Physics.Raycast(GetSpawnPosition().position + (new Vector3(0, 100, 0)), Vector3.down,
                out RaycastHit hit, Mathf.Infinity, Global.instance.gameLayer))
            {
                Entity target = Global.instance.FindEntityFromObject(hit.collider.gameObject);
                bool blocked = DislodgeBlocker(target);
                if (blocked) productionBlocked = true;
                if (!blocked && num >= fac.consumePopulationAmount)
                {
                    productionBlocked = false;
                }
            }
            if (fac.spawnTimer >= fac.maxSpawnTimeCost && !productionBlocked) //ready to spawn
            {
                BuildQueueSpawn(fac);
                //spawn the unit 
            }
        }

        if (GetProductionBar() != null)
        {
            if (buildQueue.Count > 0)
            {
                GetProductionBar().SetVisible(true);
                FactionUnit fac = buildQueue[0];
                if (fac != null)
                {
                    GetProductionBar().SetRatioBasedOnProduction(fac.spawnTimer, fac.maxSpawnTimeCost);
                }
            }
            else
            {
                GetProductionBar().SetVisible(false);
            }
        }
    }
    private void BuildQueueSpawn(FactionUnit unit)
    {
        buildQueue.RemoveAt(0);
        SpawnFromSpawner(this, rallyPoint, unit);
        RTSPlayer player = GetController() as RTSPlayer;
        player.UpdateSpawnerButtons();
    }
    public void UpdateRallyVariables()
    {
        if (rallyTarget != null) //update rally visual and rally point to rally target position
        {
            rallyVisual.transform.position = rallyTarget.transform.position;
            rallyPoint = rallyTarget.transform.position;
        }
    }
    public void UpdateRallyVisual(bool val)
    {
        if (rallyVisual != null)
        {
            rallyVisual.transform.position = rallyPoint;
            rallyVisual.SetActive(val);
        }
        if (GetLineIndicator() != null)
        {
            GetLineIndicator().enabled = val;
        }
    }
    public void SetRally() //later have this take in a vector3?
    {
        spawnerRallyMission = RallyMission.Move;
        rallyTarget = null;
        //determine if spawned units should be given a mission

        Ray ray = Global.instance.localPlayer.mainCam.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray.origin, ray.direction, out RaycastHit hit, Mathf.Infinity, Global.instance.gameLayer))
        {
            rallyPoint = hit.point;
            //Debug.Log("Setting rally" + rallyPoint);
            if (rallyVisual != null)
            {
                rallyVisual.transform.position = rallyPoint;

            }
            if (GetLineIndicator() != null)
            {
                Vector3 offset = new Vector3(0, 0.01f, 0);
                GetLineIndicator().SetPosition(0, transform.position + offset);
                GetLineIndicator().SetPosition(1, rallyPoint + offset);
            }
            Entity target = Global.instance.FindEntityFromObject(hit.collider.gameObject);
            //SelectableEntity target = hit.collider.GetComponent<SelectableEntity>();
            if (target != null)
            {
                if (target.teamType == TeamBehavior.OwnerTeam)
                {
                    if (target.net.OwnerClientId == ent.net.OwnerClientId) //same team
                    {
                        if (target.fullyBuilt && target.HasEmptyGarrisonablePosition()) //target can be garrisoned
                        {
                            spawnerRallyMission = RallyMission.Garrison;
                            rallyTarget = target;
                        }
                        else //clicking on structure causes us to try to build
                        {
                            spawnerRallyMission = RallyMission.Build;
                            rallyTarget = target;
                        }
                    }
                    else //enemy
                    {
                        spawnerRallyMission = RallyMission.Attack;
                        rallyTarget = target;
                    }
                }
                else if (target.teamType == TeamBehavior.FriendlyNeutral)
                {
                    if (target.IsOre())
                    {
                        spawnerRallyMission = RallyMission.Harvest;
                        rallyTarget = target;
                    }
                }
            }
        }
    }
    public void SpawnFromSpawner(Spawner spawner, Vector3 rally, FactionUnit unit)
    {
        GetController().ChangePopulation(unit.consumePopulationAmount);
        //spawner is this
        Vector3 pos;
        if (spawner.GetSpawnPosition() != null)
        {
            pos = new Vector3(spawner.GetSpawnPosition().position.x, 0, spawner.GetSpawnPosition().position.z);
        }
        else
        {
            pos = spawner.transform.position;
        }
        if (GetController() is RTSPlayer)
        {
            RTSPlayer rts = GetController() as RTSPlayer;
            rts.GenericSpawnMinion(pos, unit, this);
        }
        else if (GetController() is AIPlayer)
        {
            AIPlayer ai = GetController() as AIPlayer;
            ai.SpawnMinion(pos, unit);
        }
    }
}

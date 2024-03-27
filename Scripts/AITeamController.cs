using System.Collections;
using System.Collections.Generic;
using UnityEngine; 

public class AITeamController : MonoBehaviour
{
    public List<SelectableEntity> ownedEntities = new();

    public float actionTime = 4;
    private float timer = 0;
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        timer += Time.deltaTime;
        if (timer >= actionTime)
        {
            timer = 0;
            PerformAction();
        }
    }
    private void PerformAction()
    {
        MoveAllUnits();
    }
    private void MoveAllUnits()
    {
        int max = Global.Instance.maxMapSize;
        Vector3 randomTarget = new Vector3(Random.Range(-max, max), 0, Random.Range(-max, max));
        foreach (SelectableEntity item in ownedEntities)
        {
            if (item.minionController != null) //minion
            {
                item.minionController.AIAttackMove(randomTarget);
            }
        }
    }
    
}

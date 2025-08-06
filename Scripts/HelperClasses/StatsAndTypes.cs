public enum ResourceType
{
    None, 
    All,
    Gold, //used for everything
    Wood, //used for structures
    Cactus, //special resource
    Quicksilver, //special resource
}
public enum Stat
{
    ArmySize, MaxHP
}

[System.Serializable]
public class ResourceQuantity
{
    public ResourceType resource;
    public int quantity = 1;
}
[System.Serializable]
public class Stats
{
    public Stat stat;
    public int add = 0;
}
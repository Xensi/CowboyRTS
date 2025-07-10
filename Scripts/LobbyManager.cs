using System.Collections;
using System.Collections.Generic;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using Unity.Netcode;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using System.Threading.Tasks;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport.Relay;
#if UNITY_EDITOR 
using ParrelSync;
#endif 
public class LobbyManager : MonoBehaviour
{

    private Lobby hostLobby;
    private Lobby joinedLobby;
    private float heartbeatTimer = 0;

    private float pollTimer = 0;
    public TMP_InputField playerNameField;
    public TMP_InputField lobbyNameField;

    public string joinCode;

    private string playerName;
    public UnityTransport relayTransport;
    public JoinLobbyButton joinLobbyButtonPrefab;
    public Transform lobbiesStart;

    public Button leaveLobbyButton;
    public Button createLobbyButton;
    public Button startGameButton;
    public Button startSPGameButton;

    public TMP_Text playersInLobby;
    private const string PLAYERNAME = "PlayerName";
    private const string STARTGAME = "StartGame";

    #region Instance
    public static LobbyManager Instance { get; private set; }
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
        }
        else
        {
            Instance = this;
        }
    }
    #endregion

    private async void Start()
    {
        if (playerName == "") playerName = "anonymous";
        playerName = PlayerPrefs.GetString(PLAYERNAME);
        playerNameField.text = playerName;
        await Authenticate(); //sign in
        playerNameField.onEndEdit.AddListener(delegate { SavePlayerName(playerNameField.text); });
        //NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnectedCallback;
        //NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnectCallback;
        HostSingleplayer();
    }
    void OnApplicationQuit()
    {
        if (hostLobby != null)
        {
            DeleteLobby(hostLobby);
        }
    }
    private void Update()
    {
        HandleLobbyHeartBeat();
        HandleLobbyPollForUpdates();
        UpdateButtons();
    }
    #region Multiplayer
    private void SavePlayerName(string name)
    {
        PlayerPrefs.SetString(PLAYERNAME, name);
        playerName = name;
        playerNameField.text = playerName;
    }
    private void UpdateButtons()
    {
        if (leaveLobbyButton != null)
        {
            leaveLobbyButton.interactable = joinedLobby != null;
        }
        if (createLobbyButton != null)
        {
            createLobbyButton.interactable = joinedLobby == null;
        }
        if (startGameButton != null)
        {
            startGameButton.interactable = hostLobby != null;
        }
    }

    private async Task Authenticate()
    {
        var options = new InitializationOptions();

#if UNITY_EDITOR 
        // It's used to differentiate the clients, otherwise lobby will count them as the same
        options.SetProfile(ClonesManager.IsClone() ? ClonesManager.GetArgument() : "Primary");
#endif

        await UnityServices.InitializeAsync(options);

        AuthenticationService.Instance.SignedIn += () => //on sign in:
        {
            Debug.Log("Signed in. PLAYER ID:" + AuthenticationService.Instance.PlayerId);
        };

        await AuthenticationService.Instance.SignInAnonymouslyAsync();
    }
    private List<JoinLobbyButton> lobbyButtons = new();
    private float queryRateLimit = 1.1f;
    public async void RefreshLobbies()
    {
        if (pollTimer < queryRateLimit)
        {
            pollTimer = queryRateLimit;
        }
        try
        {
            QueryLobbiesOptions queryLobbiesOptions = new QueryLobbiesOptions
            {
                Count = 25,
                Filters = new List<QueryFilter> {
                    new QueryFilter(QueryFilter.FieldOptions.AvailableSlots, "0", QueryFilter.OpOptions.GT), //only include lobbies with greater than 0 available slots
                    //new QueryFilter(QueryFilter.FieldOptions.S1, "CaptureTheFlag", QueryFilter.OpOptions.EQ), //only include lobbies of this game type
                },
                Order = new List<QueryOrder> {
                    new QueryOrder(false, QueryOrder.FieldOptions.Created)
                }
            };
            QueryResponse queryResponse = await Lobbies.Instance.QueryLobbiesAsync();
            //Debug.Log("Lobbies found " + queryResponse.Results.Count);
            int i = 0;
            int height = 30 * 2;
            int spacing = 10;
            foreach (JoinLobbyButton item in lobbyButtons)
            {
                Destroy(item.gameObject);
            }
            lobbyButtons.Clear();
            foreach (Lobby lobby in queryResponse.Results) //for each lobby, create a button we can click to join the lobby
            {
                if (joinedLobby != null && lobby == joinedLobby) // skip if it's our lobby 
                {
                    continue;
                }
                //Debug.Log(lobby.Name + " " + lobby.MaxPlayers);
                JoinLobbyButton lobbyButton = Instantiate(joinLobbyButtonPrefab, lobbiesStart);
                lobbyButton.transform.localPosition += new Vector3(0, -i * (height - spacing), 0);
                lobbyButton.text.text = lobby.Name + ": " + lobby.Players.Count + "/" + lobby.MaxPlayers;
                lobbyButton.lobbyId = lobby.Id;
                lobbyButtons.Add(lobbyButton);
                i++;
            }
        }
        catch (LobbyServiceException e)
        {
            Debug.Log(e);
        }
    }
    private void UpdateLobbyPlayerNames()
    {
        playersInLobby.text = "";
        if (joinedLobby != null)
        {
            foreach (Unity.Services.Lobbies.Models.Player player in joinedLobby.Players)
            {
                playersInLobby.text += player.Data[PLAYERNAME].Value + "\n";
            }
        }
    }
    public async void CreateLobby() //and creator will join it
    {
        try
        {
            if (hostLobby == null) //can't make a lobby if we are already hosting
            {
                string lobbyName;
                if (lobbyNameField.text != "")
                {
                    lobbyName = lobbyNameField.text;

                }
                else
                {
                    lobbyName = "Default Lobby Name";
                }
                int maxPlayers = 4;

                CreateLobbyOptions createLobbyOptions = new CreateLobbyOptions
                {
                    IsPrivate = false,
                    Player = GetPlayer(),
                    Data = new Dictionary<string, DataObject>
                {
                    { "GameMode", new DataObject(DataObject.VisibilityOptions.Public, "CaptureTheFlag") }, //, DataObject.IndexOptions.S1
                    { "Map", new DataObject(DataObject.VisibilityOptions.Public, "Default Map") },
                    { STARTGAME, new DataObject(DataObject.VisibilityOptions.Member, "0") } //default 0
                }
                };
                Lobby lobby = await LobbyService.Instance.CreateLobbyAsync(lobbyName, maxPlayers, createLobbyOptions);

                hostLobby = lobby;
                joinedLobby = hostLobby;

                Debug.Log("Created lobby! " + lobby.Name + " " + lobby.MaxPlayers + " " + lobby.Id + " " + lobby.LobbyCode);
                PrintPlayers(hostLobby);
            }
        }
        catch (LobbyServiceException e)
        {
            Debug.Log(e);
        }
    }
    public async void JoinLobbyByCode()
    {
        try
        {
            JoinLobbyByCodeOptions joinOptions = new()
            {
                Player = GetPlayer(),
            };
            Lobby lobby = await Lobbies.Instance.JoinLobbyByCodeAsync(joinCode, joinOptions); //join first lobby found
            joinedLobby = lobby;
            Debug.Log("Joined lobby with code " + joinCode);
            PrintPlayers(lobby);
        }
        catch (LobbyServiceException e)
        {
            Debug.Log(e);
        }
    }
    public async void JoinLobbyById(string id)
    {
        try
        {
            JoinLobbyByIdOptions joinOptions = new()
            {
                Player = GetPlayer(),
            };
            Lobby lobby = await Lobbies.Instance.JoinLobbyByIdAsync(id, joinOptions); //join first lobby found
            joinedLobby = lobby;
            Debug.Log("Joined lobby with id " + id);
            PrintPlayers(lobby);
        }
        catch (LobbyServiceException e)
        {
            Debug.Log(e);
        }
    }
    public async void StartGame() //begins the game for the host, and sends relay code to other players in lobby so they can join
    {
        try
        {
            Debug.Log("StartGame");
            string relayCode = await CreateRelay();
            Lobby lobby = await Lobbies.Instance.UpdateLobbyAsync(joinedLobby.Id, new UpdateLobbyOptions //create lobby
            {
                Data = new Dictionary<string, DataObject>
                {
                    { STARTGAME, new DataObject(DataObject.VisibilityOptions.Member, relayCode) } //share relay code so others can join
                }
            });
            ChangeLobbyUIStatus(false);
            ChangeGameUIStatus(true);
        }
        catch (LobbyServiceException e)
        {
            Debug.Log(e);
        }
    }
    private void ChangeLobbyUIStatus(bool val)
    {
        UIManager.instance.ChangeLobbyUIStatus(val);
    }
    private void ChangeGameUIStatus(bool val)
    {
        UIManager.instance.ChangeGameUIStatus(val);
    }
    private async Task<string> CreateRelay()
    {
        try
        {
            const int maxPlayers = 2;

            // Create a relay allocation and generate a join code to share with the lobby
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(maxPlayers);
            string joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            Debug.Log(joinCode);

            RelayServerData relayServerData = new RelayServerData(allocation, "dtls");
            relayTransport.SetRelayServerData(relayServerData);
            NetworkManager.Singleton.StartHost(); //Start the room
            return joinCode;
        }
        catch (Exception)
        {
            Debug.LogFormat("Failed creating a lobby");
            return "failed";
        }
    }
    /*public void InputFieldUpdate()
    {
        if (inputField != null)
        {
            joinCode = inputField.text;
        }
    }*/
    private async void HandleLobbyHeartBeat() //prevent the lobby from closing after 30 seconds
    {
        heartbeatTimer -= Time.deltaTime;
        if (heartbeatTimer < 0)
        {
            float heartBeatTimerMax = 15;
            heartbeatTimer = heartBeatTimerMax;

            if (hostLobby != null)
            {
                await LobbyService.Instance.SendHeartbeatPingAsync(hostLobby.Id);
            }
        }
    }
    private async void HandleLobbyPollForUpdates()
    {

        pollTimer -= Time.deltaTime;
        if (pollTimer < 0)
        {
            pollTimer = queryRateLimit;
            if (joinedLobby != null)
            {
                Lobby lobby = await LobbyService.Instance.GetLobbyAsync(joinedLobby.Id); //refresh
                joinedLobby = lobby; //necessary to set lobby again because it does not auto update

                if (joinedLobby.Data[STARTGAME].Value != "0" && !NetworkManager.Singleton.IsClient)
                {
                    //start the game
                    if (!NetworkManager.Singleton.IsServer && hostLobby == null)
                    {
                        JoinRelay(joinedLobby.Data[STARTGAME].Value);
                        joinedLobby = null;
                        ChangeLobbyUIStatus(false);
                        ChangeGameUIStatus(true);
                    }
                    else
                    {
                        Debug.LogError("We are host when we should be client.");
                    }
                }
            }

            UpdateLobbyPlayerNames();
            RefreshLobbies();
        }
    }
    private async void JoinRelay(string joinCode)
    {
        try
        {
            // If we found one, grab the relay allocation details
            JoinAllocation allocation = await RelayService.Instance.JoinAllocationAsync(joinCode);

            RelayServerData relayServerData = new RelayServerData(allocation, "dtls");
            relayTransport.SetRelayServerData(relayServerData);

            // Set the details to the transform
            //SetTransformAsClient(a);

            // Join the game room as a client
            NetworkManager.Singleton.StartClient();
            if (NetworkManager.Singleton.IsClient)
            {

                Debug.Log("Joined relay successfully");
            }
        }
        catch (Exception)
        {
            Debug.Log($"No lobbies available via quick join");
        }
    }
    private Unity.Services.Lobbies.Models.Player GetPlayer() //return a player with player name set
    {
        return new Unity.Services.Lobbies.Models.Player
        {
            Data = new Dictionary<string, PlayerDataObject>
                    {
                        { PLAYERNAME, new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, playerName) }
                    }
        };
    }


    public async void QuickJoinLobby()
    {
        try
        {
            await LobbyService.Instance.QuickJoinLobbyAsync();
        }
        catch (LobbyServiceException e)
        {
            Debug.Log(e);
        }
    }

    private void PrintPlayers()
    {
        PrintPlayers(joinedLobby);
    }
    private void PrintPlayers(Lobby lobby)
    {
        Debug.Log("Lobby name: " + lobby.Name + " " + lobby.Data["GameMode"].Value + " " + lobby.Data["Map"].Value);
        foreach (Unity.Services.Lobbies.Models.Player player in lobby.Players)
        {
            Debug.Log(player.Id + " " + player.Data[PLAYERNAME].Value);
        }
    }
    private async void UpdateLobbyGameMode(string gameMode)
    {
        try
        {
            hostLobby = await Lobbies.Instance.UpdateLobbyAsync(hostLobby.Id, new UpdateLobbyOptions
            {
                Data = new Dictionary<string, DataObject>
                { //only include data we want to update
                    { "GameMode", new DataObject(DataObject.VisibilityOptions.Public, gameMode) }
                }
            });
            joinedLobby = hostLobby;
            PrintPlayers(hostLobby);
        }
        catch (LobbyServiceException e)
        {
            Debug.Log(e);
        }
    }

    private async void UpdatePlayerName(string name)
    {
        try
        {
            playerName = name;
            await LobbyService.Instance.UpdatePlayerAsync(joinedLobby.Id, AuthenticationService.Instance.PlayerId, new UpdatePlayerOptions
            {
                Data = new Dictionary<string, PlayerDataObject>
                {
                    {PLAYERNAME, new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, playerName) }
                }
            });
        }
        catch (LobbyServiceException e)
        {
            Debug.Log(e);
        }
    }
    public async void LeaveLobby() //lobby has automatic host migration. auto deletes lobby
    {
        try
        {
            await LobbyService.Instance.RemovePlayerAsync(joinedLobby.Id, AuthenticationService.Instance.PlayerId);
            joinedLobby = null;
            hostLobby = null;
        }
        catch (LobbyServiceException e)
        {
            Debug.Log(e);
        }
    }

    private async void KickPlayer() //kick the second player
    {
        try
        {
            await LobbyService.Instance.RemovePlayerAsync(joinedLobby.Id, joinedLobby.Players[1].Id);
        }
        catch (LobbyServiceException e)
        {
            Debug.Log(e);
        }
    }
    private async void MigrateLobbyHost()
    {
        try
        {
            hostLobby = await Lobbies.Instance.UpdateLobbyAsync(hostLobby.Id, new UpdateLobbyOptions
            {
                HostId = joinedLobby.Players[1].Id
            });
            joinedLobby = hostLobby;
            PrintPlayers(hostLobby);
        }
        catch (LobbyServiceException e)
        {
            Debug.Log(e);
        }
    }
    private async void DeleteLobby(Lobby lobby)
    {
        try
        {
            await LobbyService.Instance.DeleteLobbyAsync(lobby.Id);
        }
        catch (LobbyServiceException e)
        {
            Debug.Log(e);
        }
    }
    #endregion

    #region Singleplayer

    public UnityTransport singleplayerTransport;
    /*public void StartSinglePlayerGame(Level level)
    {
        string levelName = LevelManager.instance.GetLevelName(level);

        LevelManager.instance.LoadLevel(level, HostSingleplayer);
        if (startSPGameButton != null) startSPGameButton.gameObject.SetActive(false);
    }*/
    public void HostSingleplayer()
    {
        NetworkManager.Singleton.NetworkConfig.NetworkTransport = singleplayerTransport;
        NetworkManager.Singleton.StartHost();
        //Debug.Log("How many players?" + Global.Instance.allPlayers.Count);
    }
    public void JoinLocalHost()
    {
        if (!NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsServer)
        {
            NetworkManager.Singleton.NetworkConfig.NetworkTransport = singleplayerTransport;
            NetworkManager.Singleton.StartClient();
            ChangeGameUIStatus(true);
            ChangeLobbyUIStatus(false);
        }
    }
    #endregion
}

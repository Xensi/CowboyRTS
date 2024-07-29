using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class JoinLobbyButton : MonoBehaviour
{
    public string lobbyId;
    public Button button;
    public TMP_Text text;
    public void TryToJoinThisLobby()
    {
        LobbyManager.Instance.JoinLobbyById(lobbyId);
    }
}

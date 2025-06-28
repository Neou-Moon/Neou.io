using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Photon.Pun;
using Photon.Realtime;
using System.Linq;

public class FriendsManager : MonoBehaviourPunCallbacks
{
    [SerializeField] private TMP_InputField friendInputField;
    [SerializeField] private UnityEngine.UI.Button sendFriendRequestButton;
    [SerializeField] private UnityEngine.UI.Button acceptFriendRequestButton;
    [SerializeField] private UnityEngine.UI.Button rejectFriendRequestButton;
    [SerializeField] private UnityEngine.UI.Button inviteToPartyButton;
    [SerializeField] private UnityEngine.UI.Button joinTeamMoonRanButton;
    [SerializeField] private UnityEngine.UI.Button backButton;
    [SerializeField] private RectTransform friendListContent;
    [SerializeField] private RectTransform friendRequestContent;
    [SerializeField] private RectTransform partyMemberContent;
    [SerializeField] private UnityEngine.UI.Button friendButtonPrefab;
    [SerializeField] private TextMeshProUGUI feedbackText;
    [SerializeField] private TextMeshProUGUI partyStatusText;

    private string selectedFriend = "";
    private List<string> friends = new List<string>();
    private List<string> pendingRequests = new List<string>();
    private Dictionary<string, PartyMember> partyMembers = new Dictionary<string, PartyMember>();
    private string partyId = "";
    private bool isPartyLeader = false;
    private Dictionary<string, string> friendUids = new Dictionary<string, string>();
    private Dictionary<string, bool> friendOnlineStatus = new Dictionary<string, bool>();
    private List<PartyInvite> pendingPartyInvites = new List<PartyInvite>();

    public class PartyMember
    {
        public string Username { get; set; }
        public bool IsOnline { get; set; }
    }

    public class PartyInvite
    {
        public string InviterUid { get; set; }
        public string InviterUsername { get; set; }
        public string PartyId { get; set; }
    }

    [DllImport("__Internal")]
    private static extern void FirebaseJoinParty(string uid, string partyId, string callbackObjectName);
    [DllImport("__Internal")]
    private static extern void FirebaseSendFriendRequest(string fromUid, string toUsername, string callbackObjectName);
    [DllImport("__Internal")]
    private static extern void FirebaseAcceptFriendRequest(string uid, string friendUid, string callbackObjectName);
    [DllImport("__Internal")]
    private static extern void FirebaseRejectFriendRequest(string uid, string friendUid, string callbackObjectName);
    [DllImport("__Internal")]
    private static extern void FirebaseLoadFriends(string uid, string callbackObjectName);
    [DllImport("__Internal")]
    private static extern void FirebaseCreateParty(string uid, string partyId, string callbackObjectName);
    [DllImport("__Internal")]
    private static extern void FirebaseInviteToParty(string partyId, string friendUid, string callbackObjectName);
    [DllImport("__Internal")]
    private static extern void FirebaseLeaveParty(string uid, string partyId, string callbackObjectName);
    [DllImport("__Internal")]
    private static extern void FirebaseCheckOnlineStatus(string friendUid, string callbackObjectName);
    [DllImport("__Internal")]
    private static extern void FirebaseLoadPartyMembers(string partyId, string callbackObjectName);

    void Start()
    {
        if (friendButtonPrefab == null)
        {
            friendButtonPrefab = Resources.Load<UnityEngine.UI.Button>("Prefabs/FriendButton");
            if (friendButtonPrefab == null)
                Debug.LogError("Start: Failed to load FriendButton prefab from Assets/Resources/Prefabs!");
        }

        if (Application.platform != RuntimePlatform.WebGLPlayer)
        {
            Debug.LogWarning("FriendsManager: Running in Editor, simulating Firebase operations.");
            if (feedbackText != null)
                feedbackText.text = "Running in Editor (Firebase simulated)";
        }

        if (sendFriendRequestButton != null)
            sendFriendRequestButton.onClick.AddListener(OnSendFriendRequest);
        if (acceptFriendRequestButton != null)
            acceptFriendRequestButton.onClick.AddListener(OnAcceptFriendRequest);
        if (rejectFriendRequestButton != null)
            rejectFriendRequestButton.onClick.AddListener(OnRejectFriendRequest);
        if (inviteToPartyButton != null)
            inviteToPartyButton.onClick.AddListener(OnInviteToParty);
        if (joinTeamMoonRanButton != null)
            joinTeamMoonRanButton.onClick.AddListener(OnJoinTeamMoonRan);
        if (backButton != null)
            backButton.onClick.AddListener(OnBackToMainMenu);

        if (Application.platform == RuntimePlatform.WebGLPlayer)
        {
            string uid = PlayerPrefs.GetString("PlayerUID", "");
            if (!string.IsNullOrEmpty(uid))
            {
                FirebaseLoadFriends(uid, gameObject.name);
                string savedPartyId = PlayerPrefs.GetString("PartyID", "");
                if (!string.IsNullOrEmpty(savedPartyId))
                {
                    FirebaseLoadPartyMembers(savedPartyId, gameObject.name);
                }
            }
            else
            {
                if (feedbackText != null)
                    feedbackText.text = "Error: Not signed in.";
            }
        }

        UpdateUI();
    }

    void OnBackToMainMenu()
    {
        if (!string.IsNullOrEmpty(partyId) && Application.platform == RuntimePlatform.WebGLPlayer)
        {
            string uid = PlayerPrefs.GetString("PlayerUID", "");
            FirebaseLeaveParty(uid, partyId, gameObject.name);
            Debug.Log($"OnBackToMainMenu: Leaving party {partyId} for UID {uid}");
        }
        PlayerPrefs.DeleteKey("PartyID");
        PlayerPrefs.DeleteKey("IsPartyLeader");
        PlayerPrefs.Save();
        SceneManager.LoadScene("InsideSpaceShip");
        Debug.Log("OnBackToMainMenu: Loading InsideSpaceShip scene");
    }

    void OnSendFriendRequest()
    {
        if (friendInputField == null)
        {
            Debug.LogError("OnSendFriendRequest: friendInputField is null!");
            return;
        }

        string toUsername = friendInputField.text.Trim();
        Debug.Log($"OnSendFriendRequest: Input username: '{toUsername}' (length: {toUsername.Length})");

        if (string.IsNullOrEmpty(toUsername))
        {
            if (feedbackText != null)
                feedbackText.text = "Enter a valid username.";
            Debug.Log("OnSendFriendRequest: Empty username entered.");
            return;
        }

        if (friendButtonPrefab == null)
        {
            friendButtonPrefab = Resources.Load<UnityEngine.UI.Button>("Prefabs/FriendButton");
            if (friendButtonPrefab == null)
                Debug.LogError("OnSendFriendRequest: Failed to load FriendButton prefab from Assets/Resources/Prefabs!");
        }

        if (Application.platform == RuntimePlatform.WebGLPlayer)
        {
            string uid = PlayerPrefs.GetString("PlayerUID", "");
            if (string.IsNullOrEmpty(uid))
            {
                if (feedbackText != null)
                    feedbackText.text = "Error: Not signed in.";
                Debug.Log("OnSendFriendRequest: No PlayerUID in WebGL mode.");
                return;
            }
            Debug.Log($"OnSendFriendRequest: Sending Firebase request for {toUsername}, UID: {uid}");
            FirebaseSendFriendRequest(uid, toUsername, gameObject.name);
        }
        else
        {
            Debug.Log($"OnSendFriendRequest: Editor simulation for username: {toUsername}");
            if (feedbackText != null)
                feedbackText.text = "Friend request sent (Editor simulation).";
            string[] testUsers = { "testuser1", "testuser2", "testuser3", "testuser4", "testuser5", "testuser6", "testuser7" };
            string[] testUids = { "uid1", "uid2", "uid3", "uid4", "uid5", "uid6", "uid7" };
            if (testUsers.Contains(toUsername.ToLower()))
            {
                int index = System.Array.IndexOf(testUsers, toUsername.ToLower());
                if (!friends.Contains(toUsername))
                {
                    Debug.Log($"OnSendFriendRequest: Adding {toUsername} to friends list.");
                    friends.Add(toUsername);
                    friendUids[toUsername] = testUids[index];
                    friendOnlineStatus[testUids[index]] = true;
                    pendingRequests.Remove(toUsername);
                    selectedFriend = toUsername;
                    UpdateUI();
                    if (feedbackText != null)
                        feedbackText.text = $"{toUsername} accepted your friend request (Editor simulation).";
                }
                else
                {
                    Debug.Log($"OnSendFriendRequest: {toUsername} already in friends list.");
                    if (feedbackText != null)
                        feedbackText.text = $"{toUsername} is already your friend (Editor simulation).";
                }
            }
            else
            {
                if (feedbackText != null)
                    feedbackText.text = "Test user not found. Use testuser1 to testuser7.";
            }
        }
    }

    void OnAcceptFriendRequest()
    {
        if (string.IsNullOrEmpty(selectedFriend))
        {
            feedbackText.text = "Select a friend request.";
            return;
        }

        string uid = PlayerPrefs.GetString("PlayerUID", "");
        if (Application.platform == RuntimePlatform.WebGLPlayer)
        {
            string friendUid = friendUids.FirstOrDefault(x => x.Key == selectedFriend).Value;
            if (!string.IsNullOrEmpty(friendUid))
            {
                FirebaseAcceptFriendRequest(uid, friendUid, gameObject.name);
            }
            else
            {
                feedbackText.text = "Error: Friend UID not found.";
            }
        }
        else
        {
            friends.Add(selectedFriend);
            pendingRequests.Remove(selectedFriend);
            selectedFriend = "";
            UpdateUI();
            feedbackText.text = $"Accepted friend request from {selectedFriend} (Editor simulation).";
        }
    }

    void OnRejectFriendRequest()
    {
        if (string.IsNullOrEmpty(selectedFriend))
        {
            feedbackText.text = "Select a friend request.";
            return;
        }

        string uid = PlayerPrefs.GetString("PlayerUID", "");
        if (Application.platform == RuntimePlatform.WebGLPlayer)
        {
            string friendUid = friendUids.FirstOrDefault(x => x.Key == selectedFriend).Value;
            if (!string.IsNullOrEmpty(friendUid))
            {
                FirebaseRejectFriendRequest(uid, friendUid, gameObject.name);
            }
            else
            {
                feedbackText.text = "Error: Friend UID not found.";
            }
        }
        else
        {
            pendingRequests.Remove(selectedFriend);
            selectedFriend = "";
            UpdateUI();
            feedbackText.text = $"Rejected friend request from {selectedFriend} (Editor simulation).";
        }
    }

    void OnInviteToParty()
    {
        if (string.IsNullOrEmpty(selectedFriend))
        {
            feedbackText.text = "Select a friend to invite.";
            Debug.Log("OnInviteToParty: No friend selected.");
            return;
        }

        string uid = PlayerPrefs.GetString("PlayerUID", "");
        partyId = PlayerPrefs.GetString("PartyID", "");

        int partySize = partyMembers.Count;
        if (partySize >= 5)
        {
            feedbackText.text = "Party is full (max 5 players).";
            Debug.Log($"OnInviteToParty: Cannot invite {selectedFriend}, party {partyId} is full ({partySize}/5).");
            return;
        }

        if (!friendUids.ContainsKey(selectedFriend))
        {
            feedbackText.text = "Error: Friend UID not found.";
            Debug.Log($"OnInviteToParty: No UID for {selectedFriend}.");
            return;
        }

        string friendUid = friendUids[selectedFriend];
        if (Application.platform == RuntimePlatform.WebGLPlayer)
        {
            FirebaseCheckOnlineStatus(friendUid, gameObject.name);
        }
        else
        {
            OnOnlineStatusReceived($"{friendUid}|true");
        }
    }

    public void OnOnlineStatusReceived(string data)
    {
        var parts = data.Split('|');
        if (parts.Length != 2)
        {
            feedbackText.text = "Error checking friend status.";
            Debug.Log($"OnOnlineStatusReceived: Invalid data format: {data}");
            return;
        }

        string friendUid = parts[0];
        bool isOnline = parts[1] == "true";
        string friendUsername = friendUids.FirstOrDefault(x => x.Value == friendUid).Key;

        friendOnlineStatus[friendUid] = isOnline;

        if (string.IsNullOrEmpty(friendUsername))
        {
            feedbackText.text = "Error: No username for UID.";
            Debug.Log($"OnOnlineStatusReceived: No username for UID {friendUid}");
            return;
        }

        if (!string.IsNullOrEmpty(selectedFriend) && selectedFriend.ToLower() == friendUsername.ToLower())
        {
            if (!isOnline)
            {
                feedbackText.text = $"{friendUsername} is not online.";
                Debug.Log($"OnOnlineStatusReceived: {friendUsername} (UID: {friendUid}) is offline.");
                return;
            }

            partyId = PlayerPrefs.GetString("PartyID", "");
            if (string.IsNullOrEmpty(partyId))
            {
                string leaderUsername = PlayerPrefs.GetString("PlayerUsername", "");
                if (string.IsNullOrEmpty(leaderUsername))
                {
                    feedbackText.text = "Error: Leader username not set.";
                    Debug.LogError("OnOnlineStatusReceived: No PlayerUsername found in PlayerPrefs.");
                    return;
                }
                partyId = leaderUsername;
                isPartyLeader = true;
                PlayerPrefs.SetString("PartyID", partyId);
                PlayerPrefs.SetInt("IsPartyLeader", 1);
                PlayerPrefs.Save();
                if (Application.platform == RuntimePlatform.WebGLPlayer)
                {
                    FirebaseCreateParty(PlayerPrefs.GetString("PlayerUID", ""), partyId, gameObject.name);
                }
                else
                {
                    feedbackText.text = $"Party created: {partyId} (Editor simulation).";
                    Debug.Log($"OnOnlineStatusReceived: Created party {partyId} (Editor simulation).");
                    string selfUid = PlayerPrefs.GetString("PlayerUID", "self_uid");
                    partyMembers[selfUid] = new PartyMember { Username = "You", IsOnline = true };
                }
            }

            if (Application.platform == RuntimePlatform.WebGLPlayer)
            {
                FirebaseInviteToParty(partyId, friendUid, gameObject.name);
            }
            else
            {
                int partySize = partyMembers.Count;
                if (partySize >= 5)
                {
                    feedbackText.text = "Party is full (max 5 players).";
                    Debug.Log($"OnOnlineStatusReceived: Cannot add {friendUsername}, party {partyId} is full ({partySize}/5).");
                    return;
                }

                partyMembers[friendUid] = new PartyMember { Username = friendUsername, IsOnline = true };
                feedbackText.text = $"{friendUsername} joined party led by: {partyId}!";
                Debug.Log($"OnOnlineStatusReceived: {friendUsername} (UID: {friendUid}) joined party led by: {partyId} (Editor simulation).");
                UpdateUI();
            }
        }
    }

    void OnJoinTeamMoonRan()
    {
        if (string.IsNullOrEmpty(partyId))
        {
            feedbackText.text = "Create or join a party first.";
            Debug.Log("OnJoinTeamMoon: No party ID, cannot join.");
            return;
        }

        int partySize = partyMembers.Count;
        if (partySize > 5)
        {
            feedbackText.text = "Party size exceeds maximum (5 players).";
            Debug.Log($"OnJoinTeamMoon: Party {partyId} has {partySize} players, exceeding max of 5.");
            return;
        }

        if (!isPartyLeader)
        {
            feedbackText.text = "Only the party leader can start a match.";
            Debug.Log($"OnJoinTeamMoon: Player is not party leader for party {partyId}, cannot start match.");
            return;
        }

        SceneManager.LoadScene("TeamLoadingMatch");
        Debug.Log($"OnJoinTeamMoon: Loading TeamLoadingMatch for party {partyId}, size={partySize}, isLeader={isPartyLeader}");
    }

    public void OnFriendRequestSent(string result)
    {
        feedbackText.text = result == "success" ? "Success!" : $"Error: {result}";
        if (result == "success")
            friendInputField.text = "";
    }

    public void OnFriendRequestAccepted(string result)
    {
        if (result == "success")
        {
            pendingRequests.Remove(selectedFriend);
            friends.Add(selectedFriend);
            selectedFriend = "";
            UpdateUI();
            feedbackText.text = "Friend request accepted!";
            if (Application.platform == RuntimePlatform.WebGLPlayer)
            {
                string uid = PlayerPrefs.GetString("PlayerUID", "");
                FirebaseLoadFriends(uid, gameObject.name);
            }
        }
        else
        {
            feedbackText.text = $"Error: {result}";
        }
    }

    public void OnFriendRequestRejected(string result)
    {
        if (result == "success")
        {
            pendingRequests.Remove(selectedFriend);
            selectedFriend = "";
            UpdateUI();
            feedbackText.text = "Friend request rejected.";
        }
        else
        {
            feedbackText.text = $"Error: {result}";
        }
    }

    public void OnFriendsLoaded(string data)
    {
        friendUids.Clear();
        friends.Clear();
        pendingRequests.Clear();

        if (string.IsNullOrEmpty(data))
        {
            UpdateUI();
            return;
        }

        var parts = data.Split('|');
        if (parts.Length == 2)
        {
            if (!string.IsNullOrEmpty(parts[0]))
            {
                foreach (string friend in parts[0].Split(','))
                {
                    var friendParts = friend.Split(':');
                    if (friendParts.Length == 2)
                    {
                        string username = friendParts[0];
                        string uid = friendParts[1];
                        friends.Add(username);
                        friendUids[username] = uid;
                    }
                }
            }
            if (!string.IsNullOrEmpty(parts[1]))
            {
                pendingRequests = parts[1].Split(',').ToList();
                pendingRequests.RemoveAll(r => string.IsNullOrEmpty(r));
            }
            UpdateUI();
        }
        else
        {
            feedbackText.text = "Error loading friends.";
        }

        if (Application.platform == RuntimePlatform.WebGLPlayer)
        {
            foreach (var friend in friendUids)
            {
                FirebaseCheckOnlineStatus(friend.Value, gameObject.name);
            }
        }
    }

    public void AcceptPartyInvite(PartyInvite invite)
    {
        string uid = PlayerPrefs.GetString("PlayerUID", "");
        if (string.IsNullOrEmpty(uid))
        {
            feedbackText.text = "Error: Not signed in.";
            Debug.Log("AcceptPartyInvite: No PlayerUID.");
            return;
        }

        if (Application.platform == RuntimePlatform.WebGLPlayer)
        {
            FirebaseJoinParty(uid, invite.PartyId, gameObject.name);
        }
        else
        {
            partyId = invite.PartyId;
            isPartyLeader = false;
            PlayerPrefs.SetString("PartyID", partyId);
            PlayerPrefs.SetInt("IsPartyLeader", 0);
            PlayerPrefs.Save();

            partyMembers[uid] = new PartyMember { Username = "You", IsOnline = true };
            pendingPartyInvites.Remove(invite);
            feedbackText.text = $"Joined {invite.InviterUsername}'s party!";
            Debug.Log($"AcceptPartyInvite: Joined party led by: {partyId} (Editor simulation).");
            UpdateUI();
        }
    }

    public void OnPartyCreated(string result)
    {
        if (result == "success")
        {
            feedbackText.text = "Party created!";
            if (Application.platform == RuntimePlatform.WebGLPlayer)
            {
                FirebaseLoadPartyMembers(partyId, gameObject.name);
            }
            UpdateUI();
        }
        else
        {
            feedbackText.text = $"Error creating party: {result}";
            partyId = "";
            isPartyLeader = false;
            PlayerPrefs.DeleteKey("PartyID");
            PlayerPrefs.DeleteKey("IsPartyLeader");
            PlayerPrefs.Save();
        }
    }

    public void OnPartyInvited(string result)
    {
        if (result == "success")
        {
            feedbackText.text = $"Success inviting {selectedFriend} to party!";
            if (Application.platform == RuntimePlatform.WebGLPlayer)
            {
                FirebaseLoadPartyMembers(partyId, gameObject.name);
            }
        }
        else
        {
            feedbackText.text = $"Error: {result}";
        }
    }

    public void OnPartyInviteReceived(string data)
    {
        if (string.IsNullOrEmpty(data))
        {
            Debug.Log("OnPartyInviteReceived: Empty data received.");
            return;
        }

        var parts = data.Split('|');
        if (parts.Length != 3)
        {
            Debug.Log($"OnPartyInviteReceived: Invalid data format: {data}");
            feedbackText.text = "Error processing party invite.";
            return;
        }

        string inviterUid = parts[0];
        string inviterUsername = parts[1];
        string invitedPartyId = parts[2];

        pendingPartyInvites.Add(new PartyInvite
        {
            InviterUid = inviterUid,
            InviterUsername = inviterUsername,
            PartyId = invitedPartyId
        });

        feedbackText.text = $"Received party invite from {inviterUsername}!";
        UpdateUI();
    }

    public void OnPartyMembersLoaded(string data)
    {
        partyMembers.Clear();
        if (!string.IsNullOrEmpty(data))
        {
            foreach (string member in data.Split('-'))
            {
                var parts = member.Split(':');
                if (parts.Length == 3)
                {
                    string username = parts[0];
                    string uid = parts[1];
                    bool isOnline = parts[2] == "true";
                    partyMembers[uid] = new PartyMember { Username = username, IsOnline = isOnline };
                }
            }
        }
        UpdateUI();
    }

    void UpdateUI()
    {
        Debug.Log($"UpdateUI: Friends={friends.Count}, Party={partyMembers.Count}, Invites={pendingPartyInvites.Count}, Prefab={(friendButtonPrefab != null ? "Found" : "Null")}");
        foreach (Transform child in friendListContent)
            Destroy(child.gameObject);
        foreach (Transform child in friendRequestContent)
            Destroy(child.gameObject);
        if (partyMemberContent != null)
            foreach (Transform child in partyMemberContent)
                Destroy(child.gameObject);

        if (Application.platform != RuntimePlatform.WebGLPlayer)
        {
            string[] testUsers = { "testuser1", "testuser2", "testuser3", "testuser4", "testuser5", "testuser6", "testuser7" };
            string[] testUids = { "uid1", "uid2", "uid3", "uid4", "uid5", "uid6", "uid7" };
            for (int i = 0; i < testUsers.Length; i++)
            {
                if (friendUids.ContainsKey(testUsers[i]))
                {
                    friendOnlineStatus[friendUids[testUsers[i]]] = true;
                }
            }
        }

        foreach (string friend in friends)
        {
            UnityEngine.UI.Button friendButton = Instantiate(friendButtonPrefab, friendListContent);
            friendButton.GetComponentInChildren<TextMeshProUGUI>().text = friend;
            friendButton.onClick.AddListener(() => SelectFriend(friend));

            if (friendUids.ContainsKey(friend))
            {
                string friendUid = friendUids[friend];
                bool isOnline = false;
                if (Application.platform == RuntimePlatform.WebGLPlayer)
                {
                    if (friendOnlineStatus.ContainsKey(friendUid))
                        isOnline = friendOnlineStatus[friendUid];
                }
                else
                {
                    isOnline = friendOnlineStatus.ContainsKey(friendUid) ? friendOnlineStatus[friendUid] : true;
                }
                var buttonColors = friendButton.colors;
                buttonColors.normalColor = isOnline ? Color.green : Color.red;
                buttonColors.highlightedColor = isOnline ? new Color(0f, 0.8f, 0f) : new Color(0.8f, 0f, 0f);
                buttonColors.pressedColor = isOnline ? new Color(0f, 0.6f, 0f) : new Color(0.6f, 0f, 0f);
                friendButton.colors = buttonColors;
            }

            friendButton.gameObject.SetActive(true);
        }

        foreach (string request in pendingRequests)
        {
            UnityEngine.UI.Button requestButton = Instantiate(friendButtonPrefab, friendRequestContent);
            requestButton.GetComponentInChildren<TextMeshProUGUI>().text = request;
            requestButton.onClick.AddListener(() => SelectFriend(request));
            var buttonColors = requestButton.colors;
            buttonColors.normalColor = Color.yellow;
            buttonColors.highlightedColor = new Color(0.8f, 0.8f, 0f);
            buttonColors.pressedColor = new Color(0.6f, 0.6f, 0f);
            requestButton.colors = buttonColors;
            requestButton.gameObject.SetActive(true);
        }

        if (partyMemberContent != null)
        {
            foreach (var member in partyMembers)
            {
                UnityEngine.UI.Button memberButton = Instantiate(friendButtonPrefab, partyMemberContent);
                memberButton.GetComponentInChildren<TextMeshProUGUI>().text = member.Value.Username;
                memberButton.interactable = false;
                var buttonColors = memberButton.colors;
                buttonColors.normalColor = member.Value.IsOnline ? Color.green : Color.red;
                buttonColors.highlightedColor = member.Value.IsOnline ? new Color(0f, 0.8f, 0f) : new Color(0.8f, 0f, 0f);
                buttonColors.pressedColor = member.Value.IsOnline ? new Color(0f, 0.6f, 0f) : new Color(0.6f, 0f, 0f);
                memberButton.colors = buttonColors;
                memberButton.gameObject.SetActive(true);
            }

            foreach (var invite in pendingPartyInvites)
            {
                UnityEngine.UI.Button inviteButton = Instantiate(friendButtonPrefab, partyMemberContent);
                inviteButton.GetComponentInChildren<TextMeshProUGUI>().text = $"{invite.InviterUsername} invited you to a party";
                inviteButton.onClick.AddListener(() => AcceptPartyInvite(invite));
                var buttonColors = inviteButton.colors;
                buttonColors.normalColor = Color.yellow;
                buttonColors.highlightedColor = new Color(0.8f, 0.8f, 0f);
                buttonColors.pressedColor = new Color(0.6f, 0.6f, 0f);
                inviteButton.colors = buttonColors;
                inviteButton.gameObject.SetActive(true);
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate(partyMemberContent);
        }

        LayoutRebuilder.ForceRebuildLayoutImmediate(friendListContent);
        LayoutRebuilder.ForceRebuildLayoutImmediate(friendRequestContent);

        if (partyStatusText != null)
        {
            partyStatusText.gameObject.SetActive(false);
        }
    }

    public void SelectFriend(string username)
    {
        selectedFriend = username;
        feedbackText.text = $"Selected: {username}";
    }

    void OnDestroy()
    {
        if (!string.IsNullOrEmpty(partyId) && Application.platform == RuntimePlatform.WebGLPlayer)
        {
            string uid = PlayerPrefs.GetString("PlayerUID", "");
            FirebaseLeaveParty(uid, partyId, gameObject.name);
            Debug.Log($"OnDestroy: Leaving party {partyId} for UID {uid}");
        }
        PlayerPrefs.DeleteKey("PartyID");
        PlayerPrefs.DeleteKey("IsPartyLeader");
        PlayerPrefs.Save();
    }

    public override void OnDisconnected(DisconnectCause cause)
    {
        Debug.Log($"FriendsManager: Disconnected from Photon, reason: {cause}");
        SceneManager.LoadScene("InsideSpaceShip");
    }
}
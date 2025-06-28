using ExitGames.Client.Photon;

public interface IPlayer
{
    int ActorNumber { get; }
    string NickName { get; set; }
    Hashtable CustomProperties { get; set; }
    bool IsLocal { get; }
    void AddBrightMatter(int amount);
    bool SetCustomProperties(Hashtable propertiesToSet);
    void AddPoints(int points);
    void OnPlayerKilled(string killedPlayerName);
}
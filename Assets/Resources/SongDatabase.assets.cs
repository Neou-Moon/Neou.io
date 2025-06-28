using UnityEngine;

[CreateAssetMenu(fileName = "SongDatabase", menuName = "ScriptableObjects/SongDatabase", order = 1)]
public class SongDatabase : ScriptableObject
{
    [SerializeField] private AudioClip[] songClips = new AudioClip[17]; // 17 songs
    public AudioClip[] SongClips => songClips;

    public AudioClip GetSongByName(string songName)
    {
        foreach (var clip in songClips)
        {
            if (clip != null && clip.name == songName)
            {
                return clip;
            }
        }
        return null;
    }

    public int GetSongIndex(AudioClip clip)
    {
        for (int i = 0; i < songClips.Length; i++)
        {
            if (songClips[i] == clip)
            {
                return i;
            }
        }
        return -1;
    }
}
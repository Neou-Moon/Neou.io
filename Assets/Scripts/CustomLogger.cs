using UnityEngine;

public static class CustomLogger
{
    public static bool IsDebugEnabled = true; // Set to false in production builds

    public static void Log(object message)
    {
        if (IsDebugEnabled)
            Debug.Log(message);
    }

    public static void LogWarning(object message)
    {
        if (IsDebugEnabled)
            Debug.LogWarning(message);
    }

    public static void LogError(object message)
    {
        Debug.LogError(message); // Always log errors
    }
}
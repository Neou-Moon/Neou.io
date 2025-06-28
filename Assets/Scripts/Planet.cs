using UnityEngine;
using Photon.Pun;
using System.Collections;

public class Planet : MonoBehaviourPun
{
    public GameObject halfPlanetPrefab;
    public GameObject quarterPlanetPrefab;
    private bool hasOre = false;
    private const float shrinkDuration = 2f;
    private const float shrinkScale = 0.1f;
    private const string quarterPlanetPrefabPath = "Prefabs/quarterPlanet";

    public void SetHasOre(bool value)
    {
        hasOre = value;
        CustomLogger.Log($"Planet: {gameObject.name} set hasOre={hasOre}, ViewID={photonView.ViewID}");
    }

    public void ShrinkAndDestroy()
    {
        if (!PhotonNetwork.IsMasterClient)
        {
            CustomLogger.Log($"Planet: {gameObject.name} ShrinkAndDestroy ignored, not Master Client");
            return;
        }

        StartCoroutine(ShrinkAndDestroyCoroutine());
    }

    private IEnumerator ShrinkAndDestroyCoroutine()
    {
        hasOre = false;
        CustomLogger.Log($"Planet: {gameObject.name} reset hasOre={hasOre} for shrink and destroy, ViewID={photonView.ViewID}");

        foreach (Transform child in transform)
        {
            if (child.CompareTag("Ore") && child.gameObject.GetComponent<PhotonView>() != null)
            {
                PhotonNetwork.Destroy(child.gameObject);
                CustomLogger.Log($"Planet: Destroyed ore {child.gameObject.name} on {gameObject.name} before shrinking");
            }
        }

        Vector3 initialScale = transform.localScale;
        Vector3 targetScale = initialScale * shrinkScale;
        float elapsedTime = 0f;

        while (elapsedTime < shrinkDuration)
        {
            elapsedTime += Time.deltaTime;
            float t = elapsedTime / shrinkDuration;
            transform.localScale = Vector3.Lerp(initialScale, targetScale, t);
            yield return null;
        }

        transform.localScale = targetScale;
        CustomLogger.Log($"Planet: {gameObject.name} shrunk to {targetScale} at {transform.position}");

        DestroyAndNotify();
    }

    public void SplitIntoHalves()
    {
        float offsetDistance = 0.5f;
        float downwardOffset = 0.2f;

        if (!PhotonNetwork.IsConnected)
        {
            GameObject topHalf = Instantiate(halfPlanetPrefab, transform.position + Vector3.left * offsetDistance, Quaternion.identity);
            GameObject bottomHalf = Instantiate(halfPlanetPrefab, transform.position + Vector3.right * offsetDistance + Vector3.down * downwardOffset, Quaternion.Euler(0, 0, 180));

            SetupHalfRigidbody(topHalf);
            SetupHalfRigidbody(bottomHalf);
        }
        else
        {
            if (halfPlanetPrefab == null)
            {
                CustomLogger.LogError($"Planet: {gameObject.name} has null halfPlanetPrefab.");
                return;
            }

            GameObject topHalf = PhotonNetwork.Instantiate(halfPlanetPrefab.name, transform.position + Vector3.left * offsetDistance, Quaternion.identity);
            GameObject bottomHalf = PhotonNetwork.Instantiate(halfPlanetPrefab.name, transform.position + Vector3.right * offsetDistance + Vector3.down * downwardOffset, Quaternion.Euler(0, 0, 180));

            SetupHalfRigidbody(topHalf);
            SetupHalfRigidbody(bottomHalf);
        }

        CustomLogger.Log($"Planet: {gameObject.name} split into halves at {transform.position}.");
        DestroyAndNotify();
    }

    public void SplitIntoQuarters()
    {
        if (!PhotonNetwork.IsMasterClient)
        {
            CustomLogger.Log($"Planet: {gameObject.name} SplitIntoQuarters ignored, not Master Client");
            return;
        }

        hasOre = false;
        CustomLogger.Log($"Planet: {gameObject.name} reset hasOre={hasOre} for split into quarters, ViewID={photonView.ViewID}");

        float offsetDistance = 0.5f;
        float downwardOffset = 0.2f;

        foreach (Transform child in transform)
        {
            if (child.CompareTag("Ore") && child.gameObject.GetComponent<PhotonView>() != null)
            {
                PhotonNetwork.Destroy(child.gameObject);
                CustomLogger.Log($"Planet: Destroyed ore {child.gameObject.name} on {gameObject.name} before splitting into quarters");
            }
        }

        if (quarterPlanetPrefab == null)
        {
            quarterPlanetPrefab = Resources.Load<GameObject>(quarterPlanetPrefabPath);
            if (quarterPlanetPrefab == null)
            {
                CustomLogger.LogError($"Planet: {gameObject.name} failed to load quarterPlanetPrefab from Assets/Resources/{quarterPlanetPrefabPath}.prefab");
                DestroyAndNotify();
                return;
            }
            else
            {
                CustomLogger.Log($"Planet: {gameObject.name} loaded quarterPlanetPrefab from Assets/Resources/{quarterPlanetPrefabPath}.prefab");
            }
        }

        Vector3[] quarterOffsets = new Vector3[]
        {
            new Vector3(-offsetDistance, offsetDistance, 0),
            new Vector3(offsetDistance, offsetDistance, 0),
            new Vector3(-offsetDistance, -offsetDistance - downwardOffset, 0),
            new Vector3(offsetDistance, -offsetDistance - downwardOffset, 0)
        };

        Quaternion[] quarterRotations = new Quaternion[]
        {
            Quaternion.Euler(0, 0, 45),
            Quaternion.Euler(0, 0, -45),
            Quaternion.Euler(0, 0, 135),
            Quaternion.Euler(0, 0, -135)
        };

        for (int i = 0; i < 4; i++)
        {
            GameObject quarter = PhotonNetwork.Instantiate(quarterPlanetPrefab.name, transform.position + quarterOffsets[i], quarterRotations[i]);
            SetupQuarterRigidbody(quarter);
            CustomLogger.Log($"Planet: Spawned quarter {quarter.name} at {quarter.transform.position} with rotation {quarter.transform.rotation.eulerAngles}");
        }

        CustomLogger.Log($"Planet: {gameObject.name} split into quarters at {transform.position}.");
        DestroyAndNotify();
    }

    private void DestroyAndNotify()
    {
        if (PhotonNetwork.IsConnected && photonView != null)
        {
            if (PhotonNetwork.IsMasterClient)
            {
                RandomPlanetGenerator generator = RandomPlanetGenerator.Instance;
                int retries = 0;
                const int maxRetries = 5;
                while (generator == null && retries < maxRetries)
                {
                    CustomLogger.LogWarning($"Planet: RandomPlanetGenerator.Instance not found for {gameObject.name}, retry {retries + 1}/{maxRetries}");
                    generator = RandomPlanetGenerator.Instance;
                    retries++;
                    if (generator == null)
                    {
                        System.Threading.Thread.Sleep(100); // Brief pause to avoid tight loop
                    }
                }

                if (generator != null)
                {
                    generator.photonView.RPC("RemovePlanetFromList", RpcTarget.AllBuffered, photonView.ViewID);
                    CustomLogger.Log($"Planet: Sent RPC to remove {gameObject.name} (ViewID={photonView.ViewID}) from RandomPlanetGenerator during destruction.");
                }
                else
                {
                    CustomLogger.LogError($"Planet: Failed to find RandomPlanetGenerator.Instance after {maxRetries} retries for {gameObject.name}. Ensure it exists in 'Moon Ran' scene with tag 'PlanetGenerator'.");
                }

                CustomLogger.Log($"Planet: Destroying {gameObject.name} via PhotonNetwork.Destroy, ViewID={photonView.ViewID}");
                PhotonNetwork.Destroy(gameObject);
            }
        }
        else
        {
            if (photonView == null)
            {
                CustomLogger.LogWarning($"Planet: {gameObject.name} missing PhotonView, using Destroy.");
            }
            CustomLogger.Log($"Planet: Destroying {gameObject.name} via Destroy (non-Photon)");
            Destroy(gameObject);
        }
    }

    private void SetupHalfRigidbody(GameObject half)
    {
        if (half == null)
        {
            CustomLogger.LogError($"Planet: Attempted to set up Rigidbody2D for null halfPlanet.");
            return;
        }

        Rigidbody2D halfRb = half.GetComponent<Rigidbody2D>();
        if (halfRb != null)
        {
            halfRb.mass = 100f;
            halfRb.linearDamping = 2f;
            halfRb.linearVelocity = Vector2.zero;
            CustomLogger.Log($"Planet: Spawned halfPlanet {half.name}, mass={halfRb.mass}, drag={halfRb.linearDamping}, velocity={halfRb.linearVelocity}.");
        }
        else
        {
            CustomLogger.LogError($"Planet: halfPlanet {half.name} missing Rigidbody2D component.");
        }
    }

    private void SetupQuarterRigidbody(GameObject quarter)
    {
        if (quarter == null)
        {
            CustomLogger.LogError($"Planet: Attempted to set up Rigidbody2D for null quarterPlanet.");
            return;
        }

        Rigidbody2D quarterRb = quarter.GetComponent<Rigidbody2D>();
        if (quarterRb != null)
        {
            quarterRb.mass = 50f;
            quarterRb.linearDamping = 2f;
            quarterRb.linearVelocity = Vector2.zero;
            CustomLogger.Log($"Planet: Spawned quarterPlanet {quarter.name}, mass={quarterRb.mass}, drag={quarterRb.linearDamping}, velocity={quarterRb.linearVelocity}.");
        }
        else
        {
            CustomLogger.LogError($"Planet: quarterPlanet {quarter.name} missing Rigidbody2D component.");
        }
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision.gameObject.CompareTag("Droid"))
        {
            PhotonView droidView = collision.gameObject.GetComponent<PhotonView>();
            string droidViewID = droidView != null ? droidView.ViewID.ToString() : "No PhotonView";
            Vector2 contactPoint = collision.GetContact(0).point;

            CustomLogger.Log($"Planet: {gameObject.name} (ViewID={photonView.ViewID}) collided with Droid: {collision.gameObject.name} (ViewID={droidViewID}) at {contactPoint}");
        }
    }
}   
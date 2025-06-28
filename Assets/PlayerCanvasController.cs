using UnityEngine;
using UnityEngine.UI;
using System.Collections;

public class PlayerCanvasController : MonoBehaviour
{
    [SerializeField] private Image bombIcon;
    [SerializeField] private Image droidIcon;
    private Color bombIconOriginalColor;
    private Color droidIconOriginalColor;
    private Material bombIconMaterial;
    private Material droidIconMaterial;
    private Coroutine bombRevealCoroutine;
    private Coroutine droidRevealCoroutine;

    void Awake()
    {
        InitializeIcons();
    }

    private void InitializeIcons()
    {
        if (bombIcon == null)
        {
            CustomLogger.LogError($"PlayerCanvasController: bombIcon not assigned in {gameObject.name}, disabling bomb icon functionality.");
            bombIcon = null;
        }
        else
        {
            bombIconOriginalColor = bombIcon.color;
            bombIconMaterial = new Material(bombIcon.material); // Instance material
            if (bombIconMaterial == null || bombIconMaterial.shader == null || bombIconMaterial.shader.name != "UI/GradientReveal")
            {
                CustomLogger.LogWarning($"PlayerCanvasController: bombIcon material missing or incorrect shader in {gameObject.name}, using default.");
                bombIconMaterial = new Material(Shader.Find("UI/Default"));
            }
            bombIconMaterial.SetColor("_RedColor", Color.red);
            bombIconMaterial.SetColor("_OriginalColor", bombIconOriginalColor);
            bombIconMaterial.SetFloat("_Progress", 1f);
            bombIcon.material = bombIconMaterial;
            CustomLogger.Log($"PlayerCanvasController: Initialized bombIcon with original color {bombIconOriginalColor}, progress=1");
        }

        if (droidIcon == null)
        {
            CustomLogger.LogError($"PlayerCanvasController: droidIcon not assigned in {gameObject.name}, disabling droid icon functionality.");
            droidIcon = null;
        }
        else
        {
            droidIconOriginalColor = droidIcon.color;
            droidIconMaterial = new Material(droidIcon.material); // Instance material
            if (droidIconMaterial == null || droidIconMaterial.shader == null || droidIconMaterial.shader.name != "UI/GradientReveal")
            {
                CustomLogger.LogWarning($"PlayerCanvasController: droidIcon material missing or incorrect shader in {gameObject.name}, using default.");
                droidIconMaterial = new Material(Shader.Find("UI/Default"));
            }
            droidIconMaterial.SetColor("_RedColor", Color.red);
            droidIconMaterial.SetColor("_OriginalColor", droidIconOriginalColor);
            droidIconMaterial.SetFloat("_Progress", 1f);
            droidIcon.material = droidIconMaterial;
            CustomLogger.Log($"PlayerCanvasController: Initialized droidIcon with original color {droidIconOriginalColor}, progress=1");
        }
    }

    public void StartBombIconReveal(float duration)
    {
        if (bombIcon == null || bombIconMaterial == null)
        {
            CustomLogger.LogWarning($"PlayerCanvasController: Cannot start BombIcon reveal, bombIcon or material is null.");
            return;
        }
        if (bombRevealCoroutine != null) StopCoroutine(bombRevealCoroutine);
        bombIconMaterial.SetFloat("_Progress", 0f);
        bombIcon.enabled = true;
        bombRevealCoroutine = StartCoroutine(RevealGradient(bombIconMaterial, bombIcon, duration));
        CustomLogger.Log($"PlayerCanvasController: Started BombIcon gradient reveal, duration={duration}s");
    }

    public void StartDroidIconReveal(float duration)
    {
        if (droidIcon == null || droidIconMaterial == null)
        {
            CustomLogger.LogWarning($"PlayerCanvasController: Cannot start DroidIcon reveal, droidIcon or material is null.");
            return;
        }
        if (droidRevealCoroutine != null) StopCoroutine(droidRevealCoroutine);
        droidIconMaterial.SetFloat("_Progress", 0f);
        droidIcon.enabled = true;
        droidRevealCoroutine = StartCoroutine(RevealGradient(droidIconMaterial, droidIcon, duration));
        CustomLogger.Log($"PlayerCanvasController: Started DroidIcon gradient reveal, duration={duration}s");
    }

    private IEnumerator RevealGradient(Material material, Image icon, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float progress = elapsed / duration;
            material.SetFloat("_Progress", progress);
            yield return null;
        }
        material.SetFloat("_Progress", 1f);
        yield return StartCoroutine(BlinkIcon(icon));
    }

    private IEnumerator BlinkIcon(Image icon)
    {
        if (icon == null) yield break;
        const int blinkCount = 3;
        const float totalDuration = 1f;
        float blinkInterval = totalDuration / (blinkCount * 2);
        for (int i = 0; i < blinkCount; i++)
        {
            icon.enabled = false;
            yield return new WaitForSeconds(blinkInterval);
            icon.enabled = true;
            yield return new WaitForSeconds(blinkInterval);
        }
        icon.enabled = true;
        CustomLogger.Log($"PlayerCanvasController: Completed blink for {icon.name}");
    }

    public void ResetIcons()
    {
        if (bombRevealCoroutine != null) StopCoroutine(bombRevealCoroutine);
        if (droidRevealCoroutine != null) StopCoroutine(droidRevealCoroutine);
        if (bombIcon != null && bombIconMaterial != null)
        {
            bombIconMaterial.SetFloat("_Progress", 1f);
            bombIcon.enabled = true;
        }
        if (droidIcon != null && droidIconMaterial != null)
        {
            droidIconMaterial.SetFloat("_Progress", 1f);
            droidIcon.enabled = true;
        }
        CustomLogger.Log("PlayerCanvasController: Reset bombIcon and droidIcon.");
    }
}
using System.Collections;
using UnityEngine;

public class Flashlight : MonoBehaviour
{
    [Header("Light Component")]
    [SerializeField] private Light flashlightLight; // make variable editable via inspector
    [SerializeField] private float defaultIntensity = 1.0f;
    public bool isOn = true;

    [Header("Audio Settings")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip toggleSound;
    [SerializeField] private AudioClip flickerSound;

    private bool isFlickering = false;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    private void Start()
    {
        // set light object automatically to inspector
        if(flashlightLight == null)
        {
            flashlightLight = GetComponent<Light>();
        }

        // auto find audio if one is attached
        if(audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
        }

        updateLightState();
    }

    public void toggleFlashlight()
    {
        // flip the state
        isOn = !isOn;

        //play switch sound if audio is assigned
        if(audioSource != null && toggleSound !=null)
        {
            audioSource.PlayOneShot(toggleSound);
        }
        updateLightState();
    }

    private void updateLightState()
    {
        if(flashlightLight != null)
        {
            flashlightLight.enabled = isOn;
            flashlightLight.intensity = defaultIntensity;
        }
    }

    // [ContextMenu] allow testing directly from the Unity Inspector
    [ContextMenu("Test Flicker")]
    public void triggerFlicker()
    {
        triggerFlicker(1.5f);
    }


    public void triggerFlicker(float duration = 1.5f)
    {
        if(isOn && !isFlickering)
        {
            StartCoroutine(flickerRoutine(duration));
        }
    }

    private IEnumerator flickerRoutine(float duration)
    {
        isFlickering = true;
        float elapsed = 0f;

        // play flicker sound if assigned
        if(audioSource != null && flickerSound != null)
        {
            audioSource.PlayOneShot(flickerSound);
        }
        
        while(elapsed < duration && isOn)
        {
            // drop brightness randomly
            flashlightLight.intensity = Random.Range(0.1f, defaultIntensity * 0.3f);
            yield return new WaitForSeconds(Random.Range(0.03f, 0.08f));

            // restore brightness briefly
            flashlightLight.intensity = defaultIntensity;
            yield return new WaitForSeconds(Random.Range(0.04f,0.12f));

            elapsed += 0.2f;
        }

        // reset to normal intensity when done
        if(isOn)
        {
            flashlightLight.intensity = defaultIntensity;
        }
        isFlickering = false;
    }
    // Update is called once per frame
    void Update()
    {
        
    }
}

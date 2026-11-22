using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

/// <summary>Prepares once, plays on death using unscaled time, then holds the final frame.</summary>
[DisallowMultipleComponent]
public sealed class DeathVideoPresentation : MonoBehaviour
{
    private VideoClip clip;
    private RawImage screen;
    private VideoPlayer video;
    private bool requested, failed;
    private float waitStarted;

    public void Configure(VideoClip source, RawImage target) { clip = source; screen = target; }

    private void Awake()
    {
        video = gameObject.AddComponent<VideoPlayer>();
        video.playOnAwake = false;
        video.source = VideoSource.VideoClip;
        video.clip = clip;
        video.renderMode = VideoRenderMode.APIOnly;
        video.timeUpdateMode = VideoTimeUpdateMode.UnscaledGameTime;
        video.isLooping = false;
        video.waitForFirstFrame = true;
        // Only the movie's own soundtrack plays; no legacy ECG audio is created.
        video.audioOutputMode = VideoAudioOutputMode.AudioSource;
        if (clip != null)
        {
            video.controlledAudioTrackCount = clip.audioTrackCount;
            for (ushort i = 0; i < clip.audioTrackCount; i++)
            {
                var source = gameObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 0f;
                source.ignoreListenerPause = true;
                video.SetTargetAudioSource(i, source);
                video.EnableAudioTrack(i, true);
            }
        }
        video.prepareCompleted += OnPrepared;
        video.errorReceived += OnError;
        if (clip != null) video.Prepare();
        else OnError(video, "Assign the death animation VideoClip in the scene.");
    }

    public void PlayDeath()
    {
        if (requested) return;
        requested = true;
        waitStarted = Time.unscaledTime;
        if (!failed && video.isPrepared) video.Play();
    }

    private void OnPrepared(VideoPlayer source) { if (requested && !failed) source.Play(); }
    private void OnError(VideoPlayer source, string error)
    {
        failed = true;
        Debug.LogError("Death video: " + error + ". Retry and quit remain available.", this);
    }

    private void Update()
    {
        if (!requested || failed) return;
        if (video.texture != null && video.frame >= 0 && screen != null)
        {
            if (screen.texture != video.texture) screen.texture = video.texture;
            screen.color = Color.white;
        }
        else if (Time.unscaledTime - waitStarted > 15f)
        {
            OnError(video, "Playback timed out");
            video.Stop();
        }
    }

    private void OnDisable() { if (video != null) video.Stop(); }
    private void OnDestroy()
    {
        if (video == null) return;
        video.prepareCompleted -= OnPrepared;
        video.errorReceived -= OnError;
    }
}

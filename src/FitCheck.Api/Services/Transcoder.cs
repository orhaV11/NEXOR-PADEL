namespace FitCheck.Api.Services;

/// <summary>
/// Re-encodes stored clips to H.264 MP4 in the background when ffmpeg is available, so a WebM from an Android phone
/// plays on iPhones. Stub: the transcoding builder replaces it with the ffmpeg-backed worker.
/// </summary>
public class Transcoder
{
    /// <summary>True when ffmpeg was found and Storage:Transcode is on.</summary>
    public virtual bool Available => false;

    /// <summary>Queues one stored clip. A no-op when unavailable.</summary>
    public virtual void Enqueue(Guid checkId)
    {
    }
}

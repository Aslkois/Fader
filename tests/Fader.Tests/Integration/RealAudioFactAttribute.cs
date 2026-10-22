namespace Fader.Tests.Integration;

/// <summary>
/// Tests that talk to the real Windows audio stack and briefly play a very quiet tone on the default
/// output device. They run only when FADER_AUDIO_TESTS=1 so that a plain `dotnet test` stays silent
/// and works on machines without an audio device.
/// </summary>
public sealed class RealAudioFactAttribute : FactAttribute
{
    public RealAudioFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("FADER_AUDIO_TESTS") != "1")
            Skip = "Set FADER_AUDIO_TESTS=1 to run tests against the real Windows audio stack.";
    }
}

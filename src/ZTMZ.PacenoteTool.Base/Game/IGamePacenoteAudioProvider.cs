#nullable enable

using System.Collections.Generic;

namespace ZTMZ.PacenoteTool.Base.Game;

public sealed class GamePacenoteAudioCue
{
    public string ProviderId { get; set; } = string.Empty;
    public int PacenoteType { get; set; }
    public int ModifierFlags { get; set; }
    public List<int> ModifierTypes { get; set; } = [];
}

public sealed class GamePacenoteAudioCueSpan
{
    public int PacenoteStartIndex { get; set; }
    public int PacenoteCount { get; set; }
    public GamePacenoteAudioCue Cue { get; set; } = new();
}

public sealed record GamePacenoteAudioSegment(IReadOnlyList<string> AlternativePaths);

public sealed record GamePacenoteAudioResolution(IReadOnlyList<GamePacenoteAudioSegment> Segments);

/// <summary>
/// Optional game capability that resolves source-native co-driver recordings.
/// </summary>
public interface IGamePacenoteAudioProvider
{
    void PreparePacenoteAudio();

    bool TryResolvePacenoteAudio(
        GamePacenoteAudioCue cue,
        out GamePacenoteAudioResolution resolution);

    bool TryResolveSystemAudio(
        string systemSoundToken,
        out GamePacenoteAudioSegment segment);
}

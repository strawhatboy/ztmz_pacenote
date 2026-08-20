#nullable enable

using CommunityToolkit.Mvvm.ComponentModel;
using ZTMZ.PacenoteTool.Base;

namespace ZTMZ.PacenoteTool.Avalonia.ViewModels;

/// <summary>Platform-neutral voice configuration state used by the Avalonia host.</summary>
internal sealed partial class VoiceSettingsViewModel : ObservableObject
{
    [ObservableProperty] private bool _playStartAndEndSound = Config.Instance.PlayStartAndEndSound;
    [ObservableProperty] private bool _playGoSound = Config.Instance.PlayGoSound;
    [ObservableProperty] private int _pacenoteDropoutPercentage = Config.Instance.PacenoteDropoutPercentage;
    [ObservableProperty] private bool _removeIntoAndOnto = Config.Instance.RemoveIntoAndOnto;
    [ObservableProperty] private bool _useReversePacenote = Config.Instance.UseReversePacenote;
    [ObservableProperty] private bool _playCollisionSound = Config.Instance.PlayCollisionSound;
    [ObservableProperty] private double _collisionSpeedChangeThresholdSlight = Config.Instance.CollisionSpeedChangeThreshold_Slight;
    [ObservableProperty] private double _collisionSpeedChangeThresholdMedium = Config.Instance.CollisionSpeedChangeThreshold_Medium;
    [ObservableProperty] private double _collisionSpeedChangeThresholdSevere = Config.Instance.CollisionSpeedChangeThreshold_Severe;
    [ObservableProperty] private bool _useDefaultSoundPackageByDefault = Config.Instance.UseDefaultSoundPackageByDefault;
    [ObservableProperty] private bool _useDefaultSoundPackageForFallback = Config.Instance.UseDefaultSoundPackageForFallback;
    [ObservableProperty] private bool _preloadSounds = Config.Instance.PreloadSounds;
    [ObservableProperty] private string _additionalCoDriverPackagesSearchPath = Config.Instance.AdditionalCoDriverPackagesSearchPath;
    [ObservableProperty] private string _additionalPacenotesDefinitionSearchPath = Config.Instance.AdditionalPacenotesDefinitionSearchPath;
    [ObservableProperty] private string _customPacenoteDatabasePath = Config.Instance.CustomPacenoteDatabasePath;

    partial void OnPlayStartAndEndSoundChanged(bool value) => Save(() => Config.Instance.PlayStartAndEndSound = value);
    partial void OnPlayGoSoundChanged(bool value) => Save(() => Config.Instance.PlayGoSound = value);
    partial void OnPacenoteDropoutPercentageChanged(int value) => Save(() => Config.Instance.PacenoteDropoutPercentage = value);
    partial void OnRemoveIntoAndOntoChanged(bool value) => Save(() => Config.Instance.RemoveIntoAndOnto = value);
    partial void OnUseReversePacenoteChanged(bool value) => Save(() => Config.Instance.UseReversePacenote = value);
    partial void OnPlayCollisionSoundChanged(bool value) => Save(() => Config.Instance.PlayCollisionSound = value);
    partial void OnCollisionSpeedChangeThresholdSlightChanged(double value) => Save(() => Config.Instance.CollisionSpeedChangeThreshold_Slight = (float)value);
    partial void OnCollisionSpeedChangeThresholdMediumChanged(double value) => Save(() => Config.Instance.CollisionSpeedChangeThreshold_Medium = (float)value);
    partial void OnCollisionSpeedChangeThresholdSevereChanged(double value) => Save(() => Config.Instance.CollisionSpeedChangeThreshold_Severe = (float)value);
    partial void OnUseDefaultSoundPackageByDefaultChanged(bool value) => Save(() => Config.Instance.UseDefaultSoundPackageByDefault = value);
    partial void OnUseDefaultSoundPackageForFallbackChanged(bool value) => Save(() => Config.Instance.UseDefaultSoundPackageForFallback = value);
    partial void OnPreloadSoundsChanged(bool value) => Save(() => Config.Instance.PreloadSounds = value);
    partial void OnAdditionalCoDriverPackagesSearchPathChanged(string value) => Save(() => Config.Instance.AdditionalCoDriverPackagesSearchPath = value);
    partial void OnAdditionalPacenotesDefinitionSearchPathChanged(string value) => Save(() => Config.Instance.AdditionalPacenotesDefinitionSearchPath = value);
    partial void OnCustomPacenoteDatabasePathChanged(string value) => Save(() => Config.Instance.CustomPacenoteDatabasePath = value);

    private static void Save(Action setValue)
    {
        setValue();
        Config.Instance.SaveUserConfig();
    }
}

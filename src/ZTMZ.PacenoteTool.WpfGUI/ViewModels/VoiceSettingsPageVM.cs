
using System.Collections.Generic;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;
using System.Windows.Data;
using System.Threading.Tasks;
using ZTMZ.PacenoteTool.Base.UI.Game;
using Wpf.Ui.Controls;
using System.Linq;
using System.Reflection;
using System.Windows.Controls;
using Microsoft.Win32;

namespace ZTMZ.PacenoteTool.WpfGUI.ViewModels;

public partial class VoiceSettingsPageVM : ObservableObject {

    [ObservableProperty]
    private bool _playStartAndEndSound = Config.Instance.PlayStartAndEndSound;

    partial void OnPlayStartAndEndSoundChanged(bool value)
    {
        Config.Instance.PlayStartAndEndSound = value;
        Config.Instance.SaveUserConfig();
    }

    [ObservableProperty]
    private bool _playGoSound = Config.Instance.PlayGoSound;

    partial void OnPlayGoSoundChanged(bool value)
    {
        Config.Instance.PlayGoSound = value;
        Config.Instance.SaveUserConfig();
    }

    [ObservableProperty]
    private bool _playCollisionSound = Config.Instance.PlayCollisionSound;

    partial void OnPlayCollisionSoundChanged(bool value)
    {
        Config.Instance.PlayCollisionSound = value;
        Config.Instance.SaveUserConfig();
    }

    [ObservableProperty]
    private float _collisionSpeedChangeThreshold_Slight = Config.Instance.CollisionSpeedChangeThreshold_Slight;

    partial void OnCollisionSpeedChangeThreshold_SlightChanged(float value)
    {
        Config.Instance.CollisionSpeedChangeThreshold_Slight = value;
        Config.Instance.SaveUserConfig();
    }

    [ObservableProperty]
    private float _collisionSpeedChangeThreshold_Medium = Config.Instance.CollisionSpeedChangeThreshold_Medium;

    partial void OnCollisionSpeedChangeThreshold_MediumChanged(float value)
    {
        Config.Instance.CollisionSpeedChangeThreshold_Medium = value;
        Config.Instance.SaveUserConfig();
    }

    [ObservableProperty]
    private float _collisionSpeedChangeThreshold_Severe = Config.Instance.CollisionSpeedChangeThreshold_Severe;

    partial void OnCollisionSpeedChangeThreshold_SevereChanged(float value)
    {
        Config.Instance.CollisionSpeedChangeThreshold_Severe = value;
        Config.Instance.SaveUserConfig();
    }

    [ObservableProperty]
    private bool _useDefaultSoundPackageByDefault = Config.Instance.UseDefaultSoundPackageByDefault;

    partial void OnUseDefaultSoundPackageByDefaultChanged(bool value)
    {
        Config.Instance.UseDefaultSoundPackageByDefault = value;
        Config.Instance.SaveUserConfig();
    }

    [ObservableProperty]
    private bool _useDefaultSoundPackageForFallback = Config.Instance.UseDefaultSoundPackageForFallback;

    partial void OnUseDefaultSoundPackageForFallbackChanged(bool value)
    {
        Config.Instance.UseDefaultSoundPackageForFallback = value;
        Config.Instance.SaveUserConfig();
    }

    [ObservableProperty]
    private bool _preloadSounds = Config.Instance.PreloadSounds;

    partial void OnPreloadSoundsChanged(bool value)
    {
        Config.Instance.PreloadSounds = value;
        Config.Instance.SaveUserConfig();
    }

    [ObservableProperty]
    private string _additionalCoDriverPackagesSearchPath = Config.Instance.AdditionalCoDriverPackagesSearchPath;

    partial void OnAdditionalCoDriverPackagesSearchPathChanged(string value)
    {
        Config.Instance.AdditionalCoDriverPackagesSearchPath = value;
        Config.Instance.SaveUserConfig();
    }

    [ObservableProperty]
    private string _additionalPacenotesDefinitionSearchPath = Config.Instance.AdditionalPacenotesDefinitionSearchPath;

    partial void OnAdditionalPacenotesDefinitionSearchPathChanged(string value)
    {
        Config.Instance.AdditionalPacenotesDefinitionSearchPath = value;
        Config.Instance.SaveUserConfig();
    }

    [RelayCommand]
    public void OnOpenFolderAdditionalCoDriverPackagesSearchPath()
    {
#if NET8_0_OR_GREATER

        OpenFolderDialog openFolderDialog =
            new()
            {
                Multiselect = false,
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };

        if (openFolderDialog.ShowDialog() != true)
        {
            return;
        }

        if (openFolderDialog.FolderNames.Length == 0)
        {
            return;
        }

        AdditionalCoDriverPackagesSearchPath = openFolderDialog.FolderNames.First();
#else
#endif
    }

    

    [RelayCommand]
    public void OnOpenFolderAdditionalPacenotesDefinitionSearchPath()
    {
#if NET8_0_OR_GREATER

        OpenFolderDialog openFolderDialog =
            new()
            {
                Multiselect = false,
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };

        if (openFolderDialog.ShowDialog() != true)
        {
            return;
        }

        if (openFolderDialog.FolderNames.Length == 0)
        {
            return;
        }

        AdditionalPacenotesDefinitionSearchPath = openFolderDialog.FolderNames.First();
#else
#endif
    }
}

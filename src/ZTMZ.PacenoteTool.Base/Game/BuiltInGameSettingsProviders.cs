#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ZTMZ.PacenoteTool.Base.Game;

public static class BuiltInGameSettingsProviders
{
    private static readonly IGameSettingsProvider Udp = new UdpSettingsProvider();
    private static readonly IGameSettingsProvider Memory = new MemorySettingsProvider();
    private static readonly IGameSettingsProvider Common = new CommonSettingsProvider();

    public static IGameSettingsProvider? Find(IGameConfig configuration) => configuration switch
    {
        UdpGameConfig => Udp,
        MemoryGameConfig => Memory,
        CommonGameConfigs => Common,
        _ => null
    };

    private sealed class UdpSettingsProvider : IGameSettingsProvider
    {
        private static readonly IReadOnlyList<GameSettingField> Fields =
        [
            new("address", "settings.udpListenAddress", GameSettingFieldKind.Text,
                "settings.tooltip.udpListenAddress", RequiresRestart: true),
            new("port", "settings.udpListenPort", GameSettingFieldKind.Number,
                "settings.tooltip.udpListenPort", RequiresRestart: true, Minimum: 1, Maximum: 65535)
        ];

        public Type ConfigurationType => typeof(UdpGameConfig);

        public IReadOnlyList<GameSettingField> GetFields(IGameConfig configuration) => Fields;

        public object? GetFieldValue(IGameConfig configuration, string fieldId)
        {
            var config = Require<UdpGameConfig>(configuration);
            return fieldId switch
            {
                "address" => config.IPAddress,
                "port" => config.Port,
                _ => throw UnknownField(fieldId)
            };
        }

        public void SetFieldValue(IGameConfig configuration, string fieldId, object? value, IGame game)
        {
            var config = Require<UdpGameConfig>(configuration);
            switch (fieldId)
            {
                case "address":
                    config.IPAddress = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
                    break;
                case "port":
                    config.Port = Convert.ToInt32(value, CultureInfo.InvariantCulture);
                    Config.Instance.WarnIfPortMismatch = true;
                    break;
                default:
                    throw UnknownField(fieldId);
            }
        }
    }

    private sealed class MemorySettingsProvider : IGameSettingsProvider
    {
        private static readonly IReadOnlyList<GameSettingField> Fields =
        [
            new("refresh-rate", "settings.memoryRefreshRate", GameSettingFieldKind.Number,
                "settings.tooltip.memoryRefreshRate", RequiresRestart: true, Minimum: 1, Maximum: 1000)
        ];

        public Type ConfigurationType => typeof(MemoryGameConfig);

        public IReadOnlyList<GameSettingField> GetFields(IGameConfig configuration) => Fields;

        public object? GetFieldValue(IGameConfig configuration, string fieldId) => fieldId switch
        {
            "refresh-rate" => Require<MemoryGameConfig>(configuration).RefreshRate,
            _ => throw UnknownField(fieldId)
        };

        public void SetFieldValue(IGameConfig configuration, string fieldId, object? value, IGame game)
        {
            if (fieldId != "refresh-rate")
            {
                throw UnknownField(fieldId);
            }

            Require<MemoryGameConfig>(configuration).RefreshRate =
                Convert.ToSingle(value, CultureInfo.InvariantCulture);
        }
    }

    private sealed class CommonSettingsProvider : IGameSettingsProvider
    {
        public Type ConfigurationType => typeof(CommonGameConfigs);

        public IReadOnlyList<GameSettingField> GetFields(IGameConfig configuration)
        {
            var config = Require<CommonGameConfigs>(configuration);
            var names = config.PropertyName.ToArray();
            var count = Math.Min(names.Length, config.PropertyValue.Count);
            var fields = new List<GameSettingField>(count);
            for (var index = 0; index < count; index++)
            {
                var value = config.PropertyValue[index];
                var propertyType = index < config.PropertyType.Count
                    ? config.PropertyType[index]
                    : string.Empty;
                var range = index < config.ValueRange.Count ? config.ValueRange[index] : null;
                var kind = value switch
                {
                    bool => GameSettingFieldKind.Boolean,
                    byte or short or int or long or float or double or decimal => GameSettingFieldKind.Number,
                    string when propertyType.StartsWith("file:", StringComparison.OrdinalIgnoreCase) => GameSettingFieldKind.FilePath,
                    _ => GameSettingFieldKind.Text
                };

                fields.Add(new GameSettingField(
                    index.ToString(CultureInfo.InvariantCulture),
                    names[index].Key,
                    kind,
                    names[index].Value,
                    RequiresRestart: true,
                    Minimum: range is { Count: >= 2 } ? range[0] : null,
                    Maximum: range is { Count: >= 2 } ? range[1] : null,
                    FileExtension: kind == GameSettingFieldKind.FilePath ? propertyType[5..].TrimStart('.') : null));
            }

            return fields;
        }

        public object? GetFieldValue(IGameConfig configuration, string fieldId)
        {
            var config = Require<CommonGameConfigs>(configuration);
            return config.PropertyValue[ParseIndex(fieldId, config.PropertyValue.Count)];
        }

        public void SetFieldValue(IGameConfig configuration, string fieldId, object? value, IGame game)
        {
            var config = Require<CommonGameConfigs>(configuration);
            var index = ParseIndex(fieldId, config.PropertyValue.Count);
            var current = config.PropertyValue[index];
            config.PropertyValue[index] = current is null || value is null
                ? value!
                : Convert.ChangeType(value, current.GetType(), CultureInfo.InvariantCulture);
        }

        private static int ParseIndex(string fieldId, int count)
        {
            if (!int.TryParse(fieldId, NumberStyles.None, CultureInfo.InvariantCulture, out var index)
                || index < 0
                || index >= count)
            {
                throw UnknownField(fieldId);
            }

            return index;
        }
    }

    private static T Require<T>(IGameConfig configuration) where T : class, IGameConfig =>
        configuration as T ?? throw new ArgumentException(
            $"Expected configuration type {typeof(T).FullName}.", nameof(configuration));

    private static ArgumentOutOfRangeException UnknownField(string fieldId) =>
        new(nameof(fieldId), fieldId, "Unknown game setting field.");
}

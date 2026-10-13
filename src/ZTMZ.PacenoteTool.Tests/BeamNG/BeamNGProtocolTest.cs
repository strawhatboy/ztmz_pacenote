using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using ZTMZ.PacenoteTool.Base.Game;
using ZTMZ.PacenoteTool.BeamNG;

namespace ZTMZ.PacenoteTool.Tests.BeamNG;

public class BeamNGProtocolTest
{
    [Fact]
    public void StageCompletePublishesOneTypedNormalResultButInactiveStageDoesNot()
    {
        var reader = new BeamNGGameDataReader();
        var changes = new List<GameStateChangeEvent>();
        reader.onGameStateChanged += changes.Add;
        void Send(bool active, bool finished) => reader.ProcessBridgePacket(new BeamNGRallyBridgePacket
        {
            Stage = new BeamNGRallyStage
            {
                Id = "test-result-stage", IsActive = active, IsFinished = finished, StageTimeSeconds = 123.45f
            }
        });
        Send(true, false);
        Send(true, false);
        Send(false, false);
        Assert.DoesNotContain(changes, e => e.NewGameState == GameState.RaceEnd);
        Send(true, false);
        Send(false, true);
        Send(false, true);
        var end = Assert.Single(changes.Where(e => e.NewGameState == GameState.RaceEnd));
        Assert.Equal(GameStateRaceEnd.Normal, Assert.IsType<GameStateRaceEnd>(end.Parameters[GameStateRaceEndProperty.FINISH_STATE]));
        Assert.Equal(123.45f, Assert.IsType<float>(end.Parameters[GameStateRaceEndProperty.FINISH_TIME]));
    }

    [Fact]
    public void TryParseOutGauge_MapsBeamNGTelemetryFields()
    {
        var packetBytes = new byte[BeamNGOutGaugePacket.MinimumPacketLength];
        Encoding.ASCII.GetBytes("beam").CopyTo(packetBytes.AsSpan(4, 4));
        packetBytes[10] = 4; // first=2, so this represents third gear.
        WriteSingle(packetBytes, 12, 22.5f);
        WriteSingle(packetBytes, 16, 6123f);
        WriteSingle(packetBytes, 20, 0.8f);
        WriteSingle(packetBytes, 24, 91.5f);
        WriteSingle(packetBytes, 28, 0.75f);
        WriteSingle(packetBytes, 36, 104f);
        BinaryPrimitives.WriteUInt32LittleEndian(packetBytes.AsSpan(44, 4), 1u << 2);
        WriteSingle(packetBytes, 48, 0.8f);
        WriteSingle(packetBytes, 52, 0.4f);
        WriteSingle(packetBytes, 56, 0.2f);

        var parsed = BeamNGOutGaugePacket.TryParse(packetBytes, out var packet);

        Assert.True(parsed);
        Assert.NotNull(packet);
        Assert.Equal("beam", packet.Car);
        Assert.Equal(3, packet.Gear);
        Assert.Equal(22.5f, packet.SpeedMetersPerSecond);
        Assert.Equal(6123f, packet.Rpm);
        Assert.Equal(0.8f, packet.Throttle);
        Assert.Equal(0.4f, packet.Brake);
        Assert.Equal(0.2f, packet.Clutch);
        Assert.Equal(1u << 2, packet.ShowLights);
    }

    [Fact]
    public void TryParseBridgePacket_ReadsStageAndPacenoteMetadata()
    {
        var packetBytes = Encoding.UTF8.GetBytes("""
            {
              "magic": "ZTMZ_BNG_RALLY",
              "protocolVersion": 1,
              "type": "stage",
              "status": "active",
              "vehicleName": "Hirochi Sunburst",
              "telemetry": {
                "vehicleId": 77,
                "maxRPM": 7800,
                "idleRPM": 850,
                "hasDetailedTelemetry": true,
                "steering": -0.65,
                "handBrake": 0.5,
                "maxGears": 6,
                "gLong": -0.8,
                "gLat": 1.1
              },
              "damage": {
                "vehicleId": 77,
                "current": {
                  "engine": {
                    "radiatorLeak": true
                  },
                  "body": {
                    "FL": 0.24
                  }
                },
                "changed": {
                  "engine": {
                    "radiatorLeak": true
                  }
                }
              },
              "stage": {
                "id": "small_island-intro_rally_stage1",
                "name": "Intro Rally Stage",
                "isActive": true,
                "isCountingDown": true,
                "countdownNumber": 4,
                "stageTimeSeconds": 93.4,
                "distanceMeters": 1842.7,
                "totalDistanceMeters": 5120.0,
                "completion": 0.36
              },
              "pacenote": {
                "id": "note-32",
                "name": "Left 4",
                "text": "Left 4 over crest",
                "sourceText": "Left 4 over crest",
                "sourceLanguage": "en-us",
                "audioMode": "structuredOnline",
                "structured": {
                  "schemaVersion": 3,
                  "items": {
                    "4": {
                      "type": "corner",
                      "direction": -1,
                      "intensity": 140,
                      "shape": "tightens"
                    }
                  }
                },
                "distanceToMeters": 72.0
              }
            }
            """);

        var parsed = BeamNGRallyBridgePacket.TryParse(packetBytes, out var packet);

        Assert.True(parsed);
        Assert.NotNull(packet);
        Assert.Equal(BeamNGRallyBridgePacket.SupportedProtocolVersion, packet.ProtocolVersion);
        Assert.Equal("Hirochi Sunburst", packet.VehicleName);
        Assert.NotNull(packet.Telemetry);
        Assert.Equal(77, packet.Telemetry.VehicleId);
        Assert.Equal(7800f, packet.Telemetry.MaxRPM);
        Assert.Equal(850f, packet.Telemetry.IdleRPM);
        Assert.True(packet.Telemetry.HasDetailedTelemetry);
        Assert.Equal(-0.65f, packet.Telemetry.Steering);
        Assert.Equal(0.5f, packet.Telemetry.HandBrake);
        Assert.Equal(6f, packet.Telemetry.MaxGears);
        Assert.Equal(-0.8f, packet.Telemetry.GLong);
        Assert.Equal(1.1f, packet.Telemetry.GLat);
        Assert.NotNull(packet.Damage);
        Assert.True(packet.Damage.Current["engine"]["radiatorLeak"].GetBoolean());
        Assert.Equal(0.24f, packet.Damage.Current["body"]["FL"].GetSingle());
        Assert.NotNull(packet.Stage);
        Assert.True(packet.Stage.IsActive);
        Assert.True(packet.Stage.IsCountingDown);
        Assert.Equal(4, packet.Stage.CountdownNumber);
        Assert.Equal(5120f, packet.Stage.TotalDistanceMeters);
        Assert.NotNull(packet.Pacenote);
        Assert.Equal("Left 4 over crest", packet.Pacenote.Text);
        Assert.Equal("en-us", packet.Pacenote.SourceLanguage);
        Assert.Equal("structuredOnline", packet.Pacenote.AudioMode);
        Assert.NotNull(packet.Pacenote.Structured);
        Assert.Equal("corner", packet.Pacenote.Structured.Items["4"].Type);
        Assert.Equal(-1, packet.Pacenote.Structured.Items["4"].Direction);
    }

    [Fact]
    public void TryParseBridgePacket_RejectsAnotherUdpPayload()
    {
        var packetBytes = Encoding.UTF8.GetBytes("""{"magic":"OTHER","protocolVersion":1}""");

        var parsed = BeamNGRallyBridgePacket.TryParse(packetBytes, out var packet);

        Assert.False(parsed);
        Assert.Null(packet);
    }

    [Fact]
    public void GetExportFileName_SanitizesStageIdentifier()
    {
        var fileName = BeamNGRallyStageStore.GetExportFileName("small/island:stage?1");

        Assert.Equal("small_island_stage_1.json", fileName);
    }

    [Fact]
    public void BeamNGRally_UsesPacenoteToolAudioByDefault()
    {
        var game = new BeamNGRally();

        Assert.DoesNotContain(
            typeof(IGamePacenotePlaybackControl),
            typeof(BeamNGRally).GetInterfaces());
        var udpConfig = Assert.IsType<UdpGameConfig>(game.DefaultGameConfigurations[UdpGameConfig.Name]);
        Assert.Equal(4444, udpConfig.Port);
        Assert.Equal(BeamNGGameDataReader.DefaultOutGaugePort, udpConfig.Port);
    }

    [Fact]
    public void TranslatePacenote_MapsBeamNGFreeformTextToZtmzTokens()
    {
        var translation = BeamNGPacenoteTranslator.Translate(
            "60 6 right don't cut over crest",
            ["60", "6_right", "dont_cut", "over_crest"]);

        Assert.Equal(
            ["60", "6_right", "dont_cut", "over_crest"],
            translation.Record.Pacenotes.Select(note => note.Note));
        Assert.Empty(translation.UnrecognizedWords);
    }

    [Fact]
    public void TranslatePacenote_DoesNotPartiallyTranslateNonEnglishFreeformText()
    {
        var translation = BeamNGPacenoteTranslator.Translate(
            new BeamNGRallyPacenote
            {
                SourceLanguage = "de-de",
                SourceText = "sechs rechts"
            },
            ["6_right"]);

        Assert.Empty(translation.Record.Pacenotes);
        Assert.Equal(["source-language:de-de"], translation.UnrecognizedWords);
    }

    [Fact]
    public void TranslatePacenote_MapsLanguageNeutralStructuredRecordToZtmzTokens()
    {
        var translation = BeamNGPacenoteTranslator.Translate(
            new BeamNGRallyPacenote
            {
                SourceText = "unused English display text",
                SourceLanguage = "de-de",
                Structured = new BeamNGRallyPacenoteStructured
                {
                    SchemaVersion = 3,
                    Items = new Dictionary<string, BeamNGRallyPacenoteStructuredItem>
                    {
                        ["1"] = new() { Type = "caution", Level = 2 },
                        ["2"] = new() { Type = "overCrest" },
                        ["4"] = new()
                        {
                            Type = "corner",
                            Direction = -1,
                            Intensity = 140,
                            ArcMeters = 120,
                            Shape = "tightens"
                        }
                    }
                }
            },
            ["caution", "over_crest", "4_left", "long", "tightens"]);

        Assert.Equal(
            ["caution", "over_crest", "4_left", "long", "tightens"],
            translation.Record.Pacenotes.Select(note => note.Note));
        Assert.Empty(translation.UnrecognizedWords);
    }

    [Fact]
    public void TranslatePacenote_MapsAnOptionalExportedStageWithTheSelectedCodriverVocabulary()
    {
        var stageExportPath = Environment.GetEnvironmentVariable("ZTMZ_BEAMNG_STAGE_EXPORT");
        var codriverDirectory = Environment.GetEnvironmentVariable("ZTMZ_BEAMNG_CODRIVER_DIRECTORY");
        if (string.IsNullOrWhiteSpace(stageExportPath) || string.IsNullOrWhiteSpace(codriverDirectory))
        {
            return;
        }

        Assert.True(File.Exists(stageExportPath), $"Stage export was not found: {stageExportPath}");
        Assert.True(Directory.Exists(codriverDirectory), $"Co-driver directory was not found: {codriverDirectory}");

        var stage = JsonSerializer.Deserialize<BeamNGRallyStageExport>(File.ReadAllText(stageExportPath));
        Assert.NotNull(stage);
        Assert.NotEmpty(stage.Pacenotes);

        var availableTokens = Directory.EnumerateFiles(codriverDirectory)
            .Select(Path.GetFileNameWithoutExtension)
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .Select(token => token!)
            .ToArray();
        Assert.NotEmpty(availableTokens);

        var unmapped = stage.Pacenotes
            .Select(pacenote => new
            {
                Pacenote = pacenote,
                Translation = BeamNGPacenoteTranslator.Translate(pacenote, availableTokens)
            })
            .Where(result => result.Translation.UnrecognizedWords.Count > 0)
            .Select(result => $"{result.Pacenote.Id}: {result.Pacenote.Text} ({string.Join(" ", result.Translation.UnrecognizedWords)})")
            .ToArray();

        Assert.True(unmapped.Length == 0, "Unmapped BeamNG pacenote data:" + Environment.NewLine + string.Join(Environment.NewLine, unmapped));
    }

    [Fact]
    public void TranslatePacenote_MapsEveryStandardStructuredTypeWithTheSelectedCodriverVocabulary()
    {
        var codriverDirectory = Environment.GetEnvironmentVariable("ZTMZ_BEAMNG_CODRIVER_DIRECTORY");
        if (string.IsNullOrWhiteSpace(codriverDirectory))
        {
            return;
        }

        Assert.True(Directory.Exists(codriverDirectory), $"Co-driver directory was not found: {codriverDirectory}");
        var availableTokens = Directory.EnumerateFiles(codriverDirectory)
            .Select(Path.GetFileNameWithoutExtension)
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .Select(token => token!)
            .ToArray();

        var pacenotes = BeamNGPacenoteTranslator.StandardModifierPhrases.Keys
            .Select(type => CreateStructuredPacenote(type))
            .Concat(CreateStructuredCornerPacenotes())
            .ToArray();
        var unmapped = pacenotes
            .Select(pacenote => new
            {
                Pacenote = pacenote,
                Translation = BeamNGPacenoteTranslator.Translate(pacenote, availableTokens),
                Type = pacenote.Structured!.Items["4"].Type
            })
            .Where(result => result.Translation.UnrecognizedWords.Count > 0)
            .Select(result => $"{result.Type}: {string.Join(" ", result.Translation.UnrecognizedWords)}")
            .ToArray();

        Assert.True(unmapped.Length == 0, "Unmapped standard BeamNG structured data:" + Environment.NewLine + string.Join(Environment.NewLine, unmapped));
    }

    private static BeamNGRallyPacenote CreateStructuredPacenote(string type)
    {
        return new BeamNGRallyPacenote
        {
            Structured = new BeamNGRallyPacenoteStructured
            {
                SchemaVersion = 3,
                Items = new Dictionary<string, BeamNGRallyPacenoteStructuredItem>
                {
                    ["4"] = new() { Type = type }
                }
            }
        };
    }

    private static IEnumerable<BeamNGRallyPacenote> CreateStructuredCornerPacenotes()
    {
        var bareCorner = new BeamNGRallyPacenoteStructuredItem
        {
            Type = "corner",
            Direction = -1,
            Intensity = 140,
            ArcMeters = 120
        };
        yield return CreateStructuredPacenote(bareCorner);

        foreach (var descriptor in new[] { "flat", "square", "hairpin", "openHairpin", "tightHairpin" })
        {
            yield return CreateStructuredPacenote(new BeamNGRallyPacenoteStructuredItem
            {
                Type = "corner",
                Descriptor = descriptor,
                Direction = -1,
                Intensity = 140,
                ArcMeters = 120
            });
        }

        foreach (var shape in new[]
                 {
                     "opens", "tightens", "opensOverCrest", "opensAndTightens",
                     "overCrest", "tightensDown", "tightensOverCrest", "tightensAndOpens"
                 })
        {
            yield return CreateStructuredPacenote(new BeamNGRallyPacenoteStructuredItem
            {
                Type = "corner",
                Direction = -1,
                Intensity = 140,
                ArcMeters = 120,
                Shape = shape
            });
        }
    }

    private static BeamNGRallyPacenote CreateStructuredPacenote(BeamNGRallyPacenoteStructuredItem item)
    {
        return new BeamNGRallyPacenote
        {
            Structured = new BeamNGRallyPacenoteStructured
            {
                SchemaVersion = 3,
                Items = new Dictionary<string, BeamNGRallyPacenoteStructuredItem> { ["4"] = item }
            }
        };
    }

    [Fact]
    public void BeamNGGameDataReader_RelaysEachCountdownNumber()
    {
        var reader = new BeamNGGameDataReader();
        var stateChanges = new List<GameStateChangeEvent>();
        reader.onGameStateChanged += stateChanges.Add;

        reader.ProcessBridgePacket(new BeamNGRallyBridgePacket
        {
            Stage = new BeamNGRallyStage
            {
                Id = "test-stage",
                IsCountingDown = true,
                CountdownNumber = 5
            }
        });
        reader.ProcessBridgePacket(new BeamNGRallyBridgePacket
        {
            Stage = new BeamNGRallyStage
            {
                Id = "test-stage",
                IsCountingDown = true,
                CountdownNumber = 5
            }
        });
        reader.ProcessBridgePacket(new BeamNGRallyBridgePacket
        {
            Stage = new BeamNGRallyStage
            {
                Id = "test-stage",
                IsCountingDown = true,
                CountdownNumber = 4
            }
        });
        reader.ProcessBridgePacket(new BeamNGRallyBridgePacket
        {
            Stage = new BeamNGRallyStage
            {
                Id = "test-stage",
                IsActive = true
            }
        });

        Assert.Collection(
            stateChanges,
            first =>
            {
                Assert.Equal(GameState.Unknown, first.LastGameState);
                Assert.Equal(GameState.RaceBegin, first.NewGameState);
            },
            second =>
            {
                Assert.Equal(GameState.RaceBegin, second.LastGameState);
                Assert.Equal(GameState.CountingDown, second.NewGameState);
                Assert.Equal(5, second.Parameters["number"]);
            },
            third =>
            {
                Assert.Equal(GameState.CountingDown, third.LastGameState);
                Assert.Equal(GameState.CountingDown, third.NewGameState);
                Assert.Equal(4, third.Parameters["number"]);
            },
            fourth =>
            {
                Assert.Equal(GameState.CountingDown, fourth.LastGameState);
                Assert.Equal(GameState.Racing, fourth.NewGameState);
            });
    }

    [Fact]
    public void BeamNGRally_MigratesTheLegacyIncorrectOutGaugePort()
    {
        var userFolder = Path.Combine(Path.GetTempPath(), "ZTMZPacenoteBeamNGTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(userFolder);
        try
        {
            var game = new BeamNGRally
            {
                GameConfigurations = new Dictionary<string, IGameConfig>
                {
                    [UdpGameConfig.Name] = new UdpGameConfig { Port = BeamNGGameDataReader.LegacyIncorrectOutGaugePort },
                    [BeamNGRallyBridgeConfig.Name] = new BeamNGRallyBridgeConfig { UserFolderPath = userFolder }
                }
            };

            var changed = game.MigrateGameConfiguration();

            Assert.True(changed);
            var udpConfig = Assert.IsType<UdpGameConfig>(game.GameConfigurations[UdpGameConfig.Name]);
            Assert.Equal(BeamNGGameDataReader.DefaultOutGaugePort, udpConfig.Port);
        }
        finally
        {
            Directory.Delete(userFolder, recursive: true);
        }
    }

    [Fact]
    public void RallySnapshot_ClearStageData_PreservesDashboardTelemetry()
    {
        var snapshot = new BeamNGRallySnapshot
        {
            StageId = "small-island-stage-1",
            StageName = "Small Island",
            StageActive = true,
            StageTimeSeconds = 42,
            DistanceMeters = 900,
            CurrentPacenoteText = "Right 4",
            FuelFraction = 0.75f,
            VehicleName = "Hirochi Sunburst"
        };

        snapshot.ClearStageData();

        Assert.Equal(string.Empty, snapshot.StageId);
        Assert.Equal(string.Empty, snapshot.CurrentPacenoteText);
        Assert.False(snapshot.StageActive);
        Assert.Equal(0f, snapshot.DistanceMeters);
        Assert.Equal(0.75f, snapshot.FuelFraction);
        Assert.Equal("Hirochi Sunburst", snapshot.VehicleName);
    }

    [Fact]
    public void BeamNGGameDataReader_UsesStableLabelsBeforeBridgeDataArrives()
    {
        var reader = new BeamNGGameDataReader();

        Assert.Equal("BeamNG.drive Rally", reader.TrackName);
        Assert.Equal("BeamNG.drive", reader.CarName);
    }

    [Fact]
    public void BeamNGGameDataReader_UsesRallyBridgeSteeringTelemetry()
    {
        var reader = new BeamNGGameDataReader();

        reader.ProcessBridgePacket(new BeamNGRallyBridgePacket
        {
            Telemetry = new BeamNGRallyVehicleTelemetry
            {
                VehicleId = 77,
                HasDetailedTelemetry = true,
                Steering = -0.65f,
                HandBrake = 0.5f,
                MaxGears = 6f,
                GLong = -0.8f,
                GLat = 1.1f
            }
        });

        Assert.Equal(-0.65f, reader.CurrentGameData.Steering);
        Assert.Equal(0.5f, reader.CurrentGameData.HandBrake);
        Assert.True(reader.CurrentGameData.HandBrakeValid);
        Assert.Equal(6f, reader.CurrentGameData.MaxGears);
        Assert.Equal(-0.8f, reader.CurrentGameData.G_long);
        Assert.Equal(1.1f, reader.CurrentGameData.G_lat);
        var snapshot = Assert.IsType<BeamNGRallySnapshot>(reader.CurrentGameData.GameSpecificData);
        Assert.Equal(-0.65f, snapshot.Steering);

        reader.ProcessBridgePacket(new BeamNGRallyBridgePacket
        {
            Telemetry = new BeamNGRallyVehicleTelemetry
            {
                VehicleId = 77,
                HasDetailedTelemetry = true,
                Steering = 2f
            }
        });

        Assert.Equal(1f, reader.CurrentGameData.Steering);
    }

    [Fact]
    public void BeamNGGameDataReader_ReportsOutGaugeAndRallyBridgeListeners()
    {
        var reader = new BeamNGGameDataReader();

        var listeners = reader.GetUdpListenerDiagnostics();

        Assert.Collection(
            listeners,
            outGauge =>
            {
                Assert.Equal("OutGauge", outGauge.Name);
                Assert.False(outGauge.Status.IsListening);
            },
            bridge =>
            {
                Assert.Equal("Rally bridge", bridge.Name);
                Assert.False(bridge.Status.IsListening);
                Assert.Equal($"127.0.0.1:{BeamNGRallyBridgeConfig.DefaultPort}", bridge.ConfiguredEndpoint);
            });
    }

    [Fact]
    public void BeamNGGameDataReader_MakesTheDashboardAvailableFromAnInitialDamageBaseline()
    {
        var reader = new BeamNGGameDataReader();
        var damageEvents = new List<CarDamageEvent>();
        reader.onCarDamaged += damageEvents.Add;

        reader.ProcessBridgePacket(new BeamNGRallyBridgePacket
        {
            Telemetry = new BeamNGRallyVehicleTelemetry { VehicleId = 77 },
            Damage = new BeamNGRallyDamage
            {
                VehicleId = 77,
                Current = new Dictionary<string, Dictionary<string, JsonElement>>
                {
                    ["engine"] = new Dictionary<string, JsonElement>
                    {
                        ["radiatorLeak"] = CreateJsonElement("false")
                    },
                    ["wheels"] = new Dictionary<string, JsonElement>
                    {
                        ["tireFL"] = CreateJsonElement("false")
                    }
                },
                Changed = new Dictionary<string, Dictionary<string, JsonElement>>()
            }
        });

        var dashboard = reader.CurrentGameData.CarDamage.Dashboard;
        Assert.True(dashboard.IsAvailable);
        Assert.Equal(0, dashboard.RadiatorSeverity);
        Assert.Equal(0, dashboard.FrontLeftTyreSeverity);
        Assert.Empty(damageEvents);
    }

    [Fact]
    public void BeamNGGameDataReader_MapsMiddleBodyDamageToDoorComponents()
    {
        var reader = new BeamNGGameDataReader();

        reader.ProcessBridgePacket(CreateDamagePacket(
            77,
            new Dictionary<string, Dictionary<string, JsonElement>>
            {
                ["body"] = new Dictionary<string, JsonElement>
                {
                    ["ML"] = CreateJsonElement("0.3"),
                    ["MR"] = CreateJsonElement("0.8")
                }
            },
            new Dictionary<string, Dictionary<string, JsonElement>>()));

        var dashboard = reader.CurrentGameData.CarDamage.Dashboard;
        Assert.True(dashboard.IsAvailable);
        Assert.Equal(2, dashboard.LeftDoorSeverity);
        Assert.Equal(3, dashboard.RightDoorSeverity);
    }

    [Fact]
    public void BeamNGGameDataReader_SeparatesGearboxFromUnknownPowertrainDevices()
    {
        var reader = new BeamNGGameDataReader();
        reader.ProcessBridgePacket(CreateDamagePacket(77,
            new Dictionary<string, Dictionary<string, JsonElement>>
            {
                ["powertrain"] = new()
                {
                    ["gearbox"] = CreateJsonElement("true"),
                    ["driveshaft"] = CreateJsonElement("true")
                }
            }, new Dictionary<string, Dictionary<string, JsonElement>>()));
        var damage = reader.CurrentGameData.CarDamage;
        Assert.True(damage.Dashboard.GearboxAvailable);
        Assert.True(damage.Dashboard.GearboxSeverity > 0);
        Assert.False(damage.Dashboard.EngineAvailable);
        Assert.False(damage.Dashboard.DifferentialAvailable);
        Assert.Contains(damage.RawComponents, p => p.SourceName == "driveshaft");
    }

    [Fact]
    public void BeamNGGameDataReader_KeepsAuxiliaryEngineTelemetryOutOfTheEngineFailureProjection()
    {
        var reader = new BeamNGGameDataReader();
        var damageEvents = new List<CarDamageEvent>();
        reader.onCarDamaged += damageEvents.Add;

        reader.ProcessBridgePacket(CreateDamagePacket(
            77,
            new Dictionary<string, Dictionary<string, JsonElement>>
            {
                ["engine"] = new Dictionary<string, JsonElement>
                {
                    ["inductionSystemDamaged"] = CreateJsonElement("true"),
                    ["impactDamage"] = CreateJsonElement("true")
                }
            },
            new Dictionary<string, Dictionary<string, JsonElement>>
            {
                ["engine"] = new Dictionary<string, JsonElement>
                {
                    ["inductionSystemDamaged"] = CreateJsonElement("true"),
                    ["impactDamage"] = CreateJsonElement("true")
                }
            }));

        var damage = reader.CurrentGameData.CarDamage;
        Assert.Equal(0, damage.Dashboard.EngineSeverity);
        Assert.DoesNotContain(damage.Components, component => component.Kind == CarDamageIds.Engine);
        Assert.Contains(damage.RawComponents, component => component.SourceName == "inductionSystemDamaged" && component.IsActive);
        Assert.Contains(damage.RawComponents, component => component.SourceName == "impactDamage" && component.IsActive);
        Assert.Empty(damageEvents);
    }

    [Fact]
    public void BeamNGGameDataReader_MapsLockedEngineToTheEngineFailureProjection()
    {
        var reader = new BeamNGGameDataReader();
        var damageEvents = new List<CarDamageEvent>();
        reader.onCarDamaged += damageEvents.Add;

        reader.ProcessBridgePacket(CreateDamagePacket(
            77,
            new Dictionary<string, Dictionary<string, JsonElement>>
            {
                ["engine"] = new Dictionary<string, JsonElement>
                {
                    ["engineLockedUp"] = CreateJsonElement("false")
                }
            },
            new Dictionary<string, Dictionary<string, JsonElement>>()));

        reader.ProcessBridgePacket(CreateDamagePacket(
            77,
            new Dictionary<string, Dictionary<string, JsonElement>>
            {
                ["engine"] = new Dictionary<string, JsonElement>
                {
                    ["engineLockedUp"] = CreateJsonElement("true")
                }
            },
            new Dictionary<string, Dictionary<string, JsonElement>>
            {
                ["engine"] = new Dictionary<string, JsonElement>
                {
                    ["engineLockedUp"] = CreateJsonElement("true")
                }
            }));

        var damage = reader.CurrentGameData.CarDamage;
        Assert.Equal(4, damage.Dashboard.EngineSeverity);
        Assert.Contains(damage.Components, component => component.Kind == CarDamageIds.Engine && component.IsActive);
        var damageEvent = Assert.Single(damageEvents);
        Assert.Equal(CarDamage.Engine, damageEvent.DamageType);
    }

    [Fact]
    public void BeamNGGameDataReader_AggregatesBodyworkChangesIntoOneCollisionEvent()
    {
        var reader = new BeamNGGameDataReader();
        var damageEvents = new List<CarDamageEvent>();
        reader.onCarDamaged += damageEvents.Add;

        reader.ProcessBridgePacket(CreateDamagePacket(
            77,
            new Dictionary<string, Dictionary<string, JsonElement>>
            {
                ["body"] = new Dictionary<string, JsonElement>
                {
                    ["FL"] = CreateJsonElement("0"),
                    ["FR"] = CreateJsonElement("0")
                }
            },
            new Dictionary<string, Dictionary<string, JsonElement>>()));

        reader.ProcessBridgePacket(CreateDamagePacket(
            77,
            new Dictionary<string, Dictionary<string, JsonElement>>
            {
                ["body"] = new Dictionary<string, JsonElement>
                {
                    ["FL"] = CreateJsonElement("0.3"),
                    ["FR"] = CreateJsonElement("0.8")
                }
            },
            new Dictionary<string, Dictionary<string, JsonElement>>
            {
                ["body"] = new Dictionary<string, JsonElement>
                {
                    ["FL"] = CreateJsonElement("0.3"),
                    ["FR"] = CreateJsonElement("0.8")
                }
            }));

        var damageEvent = Assert.Single(damageEvents);
        Assert.Equal(CarDamage.Collision, damageEvent.DamageType);
        Assert.Equal((int)CollisionSeverity.Severe, damageEvent.Parameters[CarDamageConstants.SEVERITY]);
    }

    [Fact]
    public void BeamNGGameDataReader_EmitsOnlyOneMechanicalEventPerTypePerDamagePacket()
    {
        var reader = new BeamNGGameDataReader();
        var damageEvents = new List<CarDamageEvent>();
        reader.onCarDamaged += damageEvents.Add;

        var baseline = new Dictionary<string, Dictionary<string, JsonElement>>
        {
            ["engine"] = new Dictionary<string, JsonElement>
            {
                ["radiatorLeak"] = CreateJsonElement("false"),
                ["waterPumpBroken"] = CreateJsonElement("false"),
                ["mainEngineBroken"] = CreateJsonElement("false"),
                ["oilStarvation"] = CreateJsonElement("false")
            }
        };
        reader.ProcessBridgePacket(CreateDamagePacket(
            77,
            baseline,
            new Dictionary<string, Dictionary<string, JsonElement>>()));

        var damaged = new Dictionary<string, Dictionary<string, JsonElement>>
        {
            ["engine"] = new Dictionary<string, JsonElement>
            {
                ["radiatorLeak"] = CreateJsonElement("true"),
                ["waterPumpBroken"] = CreateJsonElement("true"),
                ["mainEngineBroken"] = CreateJsonElement("true"),
                ["oilStarvation"] = CreateJsonElement("true")
            }
        };
        reader.ProcessBridgePacket(CreateDamagePacket(77, damaged, damaged));

        Assert.Equal(3, damageEvents.Count);
        Assert.Contains(damageEvents, damageEvent => damageEvent.DamageType == CarDamage.Radiator);
        Assert.Contains(damageEvents, damageEvent => damageEvent.DamageType == CarDamage.WaterPump);
        Assert.Single(damageEvents, damageEvent => damageEvent.DamageType == CarDamage.Engine);
    }

    [Fact]
    public void RallyBridgeMod_ProvidesBeamNGModManagerMetadata()
    {
        var assemblyDirectory = Path.GetDirectoryName(typeof(BeamNGBridgeInstaller).Assembly.Location);
        Assert.False(string.IsNullOrWhiteSpace(assemblyDirectory));
        var metadataPath = Path.Combine(
            assemblyDirectory!,
            "BeamNGMod",
            "mod_info",
            BeamNGBridgeInstaller.ModMetadataId,
            "info.json");

        Assert.True(File.Exists(metadataPath), $"Expected bundled metadata at {metadataPath}.");
        using var document = JsonDocument.Parse(File.ReadAllText(metadataPath));
        var metadata = document.RootElement;

        Assert.Equal(BeamNGBridgeInstaller.ModMetadataId, metadata.GetProperty("tagid").GetString());
        Assert.Equal("ZTMZ Pacenote Rally Bridge", metadata.GetProperty("title").GetString());
        Assert.Equal("Rally Mode integration for ZTMZ Pacenote Tool", metadata.GetProperty("tag_line").GetString());
        Assert.Equal("草帽不是猫 with Codex", metadata.GetProperty("username").GetString());
        Assert.Equal("1.1.10", metadata.GetProperty("version_string").GetString());
        Assert.Contains("gitee.com/ztmz/ztmz_pacenote", metadata.GetProperty("message").GetString());
    }

    [Fact]
    public void BeamNGGameDataReader_UsesASafeMaximumRpmBeforeBridgeTelemetryArrives()
    {
        var reader = new BeamNGGameDataReader();
        var packetBytes = new byte[BeamNGOutGaugePacket.MinimumPacketLength];
        WriteSingle(packetBytes, 16, 6123f);

        reader.onNewUdpMessage([], packetBytes);

        Assert.Equal(6123f, reader.CurrentGameData.RPM);
        Assert.Equal(10000f, reader.CurrentGameData.MaxRPM);
    }

    private static void WriteSingle(byte[] target, int offset, float value)
    {
        BinaryPrimitives.WriteInt32LittleEndian(target.AsSpan(offset, 4), BitConverter.SingleToInt32Bits(value));
    }

    private static JsonElement CreateJsonElement(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static BeamNGRallyBridgePacket CreateDamagePacket(
        int vehicleId,
        Dictionary<string, Dictionary<string, JsonElement>> current,
        Dictionary<string, Dictionary<string, JsonElement>> changed)
    {
        return new BeamNGRallyBridgePacket
        {
            Telemetry = new BeamNGRallyVehicleTelemetry { VehicleId = vehicleId },
            Damage = new BeamNGRallyDamage
            {
                VehicleId = vehicleId,
                Current = current,
                Changed = changed
            }
        };
    }
}

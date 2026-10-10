using System.Collections.Concurrent;
using System.IO.Pipes;
using Xunit;
using ZTMZ.PacenoteTool.Base.Game;
using ZTMZ.PacenoteTool.KT;

public sealed class Wrc7LiveSessionTests
{
    // Relevant definitions from the observed WRC7 voice resources. Four and
    // nine exercise the game's non-identity nine-to-six corner conversion.
    internal const string Notes = """
        PaceNotes = {
          RIGHT_3 = { DetailedSound = "PN_Right3", AccessibleSound = "PN_Right3" },
          RIGHT_4 = { DetailedSound = "PN_Right4", AccessibleSound = "PN_Right3" },
          RIGHT_5 = { DetailedSound = "PN_Right5", AccessibleSound = "PN_Right4" },
          RIGHT_6 = { DetailedSound = "PN_Right6", AccessibleSound = "PN_Right4" },
          RIGHT_7 = { DetailedSound = "PN_Right7", AccessibleSound = "PN_Right5" },
          RIGHT_8 = { DetailedSound = "PN_Right8", AccessibleSound = "PN_Right5" },
          RIGHT_9 = { DetailedSound = "PN_Right9", AccessibleSound = "PN_Right6" },
          LEFT_3 = { DetailedSound = "PN_Left3", AccessibleSound = "PN_Left3" },
          LEFT_4 = { DetailedSound = "PN_Left4", AccessibleSound = "PN_Left3" },
          LEFT_5 = { DetailedSound = "PN_Left5", AccessibleSound = "PN_Left4" },
          LEFT_6 = { DetailedSound = "PN_Left6", AccessibleSound = "PN_Left4" },
          LEFT_7 = { DetailedSound = "PN_Left7", AccessibleSound = "PN_Left5" },
          LEFT_8 = { DetailedSound = "PN_Left8", AccessibleSound = "PN_Left5" },
          HAIRPIN_RIGHT = { DetailedSound = "PN_HairpinRight", AccessibleSound = "PN_HairpinRight", Category = "HAIRPIN" },
          OPEN_HAIRPIN_RIGHT = { DetailedSound = "PN_OHairpinRight", AccessibleSound = "PN_HairpinRight", Category = "HAIRPIN" },
          ACUTE_HAIRPIN_RIGHT = { DetailedSound = "PN_AHairpinRight", AccessibleSound = "PN_HairpinRight", Category = "HAIRPIN" },
          snow = { DetailedSound = "PN_Snow_excla_02", AccessibleSound = "PN_Snow_excla_02" },
          keep_in = { DetailedSound = "PN_KeepIn", AccessibleSound = "PN_KeepIn" },
          _and = { DetailedSound = "PN_And", AccessibleSound = "PN_And" },
          _into = { DetailedSound = "PN_Into", AccessibleSound = "PN_Into" },
          tightens = { DetailedSound = "PN_Tighten", AccessibleSound = "PN_Tighten" },
          tightens_1 = { DetailedSound = "PN_Tighten1", AccessibleSound = "PN_Tighten1" },
        }
        PaceNotesMerging = {
          PN_And = {
            PN_Left3 = "PN_AndLeft3",
            PN_OHairpinRight = "PN_AndOHairpinRight",
          },
          PN_Into = {
            PN_Right3 = "PN_IntoRight3",
            PN_Left5 = "PN_IntoLeft5",
          },
        }
        """;
    private static readonly HashSet<string> Vocabulary = ["3_right", "4_right", "5_right", "6_right", "3_left", "4_left", "5_left", "hairpin_right", "open_hairpin_right", "right_acute", "snow", "keep_in", "and", "into", "tightens", "tightens_to_1"];

    [Theory]
    [InlineData("PN_RIGHT5.PAF", "5_right")]
    [InlineData("PN_INTOLEFT5.PAF", "into/5_left")]
    [InlineData("PN_LEFT4.PAF", "4_left")]
    [InlineData("PN_ANDOHAIRPINRIGHT.PAF", "and/open_hairpin_right")]
    public void PreviouslySkippedCallsUseIndependentLiveSettings(string resource, string expected)
    {
        var decoder = new Wrc7LivePacenoteDecoder(Wrc7PacenoteCatalog.Parse(Notes), Vocabulary);
        Assert.True(decoder.TryDecodeWithModes(1, 1, resource, new(true, false, false), out var result));
        Assert.Equal(expected, result!.Record.RawText);
    }

    [Fact]
    public void ChangingSettingsDoesNotRetainOldModeOrInventHairpinDetail()
    {
        var decoder = new Wrc7LivePacenoteDecoder(Wrc7PacenoteCatalog.Parse(Notes), Vocabulary);
        Assert.True(decoder.TryDecodeWithModes(1, 1, "PN_Right5", new(false, true, false), out var detailed));
        Assert.Equal("4_right", detailed!.Record.RawText);
        Assert.True(decoder.TryDecodeWithModes(1, 2, "PN_Right5", new(true, false, false), out var accessible));
        Assert.Equal("5_right", accessible!.Record.RawText);
        Assert.True(decoder.TryDecodeWithModes(1, 3, "PN_HairpinRight", new(false, true, false), out var hairpin));
        Assert.Equal("hairpin_right", hairpin!.Record.RawText);
        Assert.False(decoder.TryDecodeWithModes(1, 4, "PN_Right5", Wrc7PacenoteModes.Unknown, out _));
    }

    [Theory]
    [InlineData("PN_RIGHT3.PAF", "3_right")]
    [InlineData("PN_SNOW_EXCLA_02.PAF", "snow")]
    [InlineData("PN_KEEPIN.PAF", "keep_in")]
    [InlineData("PN_ANDLEFT3.PAF", "and/3_left")]
    [InlineData("PN_TIGHTEN.PAF", "tightens")]
    [InlineData("PN_INTORIGHT3.PAF", "into/3_right")]
    [InlineData("PN_TIGHTEN1.PAF", "tightens_to_1")]
    public void CapturedVoicesProduceCoreEventsWithoutGuessingMode(string resource, string expected)
    {
        var decoder = new Wrc7LivePacenoteDecoder(Wrc7PacenoteCatalog.Parse(Notes), Vocabulary);
        Assert.True(decoder.TryDecode(12, 123, "DATA:/SOUNDS/VOICE/EN/PACENOTES/MALE/" + resource, null, out var result));
        Assert.Equal(expected, result!.Record.RawText);
        Assert.Equal(expected.Split('/'), result.Record.Pacenotes.Select(note => note.Note));
    }

    [Fact]
    public void UsesGameGradeConversionAndRejectsAmbiguousUnknownMode()
    {
        var decoder = new Wrc7LivePacenoteDecoder(Wrc7PacenoteCatalog.Parse(Notes), Vocabulary);
        Assert.False(decoder.TryDecode(1, 1, "PN_Right4", null, out _));
        Assert.True(decoder.TryDecode(1, 1, "PN_Right4", false, out var detailed));
        Assert.Equal("3_right", detailed!.Record.RawText);
        Assert.True(decoder.TryDecode(1, 1, "PN_Right4", true, out var accessible));
        Assert.Equal("4_right", accessible!.Record.RawText);
        Assert.True(decoder.TryDecode(1, 1, "PN_Right9", false, out var nine));
        Assert.Equal("6_right", nine!.Record.RawText);
    }

    [Fact]
    public void DoesNotEmitPartialMergedCallWhenVocabularyLacksLink()
    {
        var decoder = new Wrc7LivePacenoteDecoder(Wrc7PacenoteCatalog.Parse(Notes), new HashSet<string> { "3_left" });
        Assert.False(decoder.TryDecode(1, 1, "PN_AndLeft3", null, out _));
    }

    [Fact]
    public async Task RealPipeRejectsStaleMessagesAndPublishesFreshCoreEvent()
    {
        var id = Random.Shared.Next(100000000, int.MaxValue);
        var result = new TaskCompletionSource<LivePacenoteEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        var events = new ConcurrentQueue<LivePacenoteEvent>();
        using var session = new Wrc7LiveSession(id, Wrc7PacenoteCatalog.Parse(Notes), Vocabulary, () => Wrc7PacenoteModes.Unknown,
            note => { events.Enqueue(note); result.TrySetResult(note); });
        await using var client = new NamedPipeClientStream(".", $"ZTMZ.WRC7.Live.v1.{id}", PipeDirection.Out, PipeOptions.Asynchronous);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await client.ConnectAsync(timeout.Token);
        using var writer = new StreamWriter(client) { AutoFlush = true };
        await writer.WriteLineAsync("READY");
        await session.Ready.Task.WaitAsync(timeout.Token);
        await writer.WriteLineAsync($"{Environment.TickCount64 - 2000}\tPN_SNOW_EXCLA_02.PAF");
        await writer.WriteLineAsync($"{Environment.TickCount64}\tPN_INTORIGHT3.PAF");
        Assert.Equal("into/3_right", (await result.Task.WaitAsync(timeout.Token)).Record.RawText);
        Assert.Single(events);
    }

    [Theory]
    [InlineData("MALE/PN_RLCHICANE.PAF", "left_entry_chicane")]
    [InlineData("FEMALE/PN_LRCHICANE.PAF", "right_entry_chicane")]
    [InlineData("PN_IntoRLChicane.PAF", "into/left_entry_chicane")]
    public void ChicanesUseVerifiedEntranceSideInBothModes(string resource, string expected)
    {
        const string source = """
            PaceNotes = {
              CHICANE_LEFT = { DetailedSound = "PN_LRChicane", AccessibleSound = "PN_LRChicane" },
              CHICANE_RIGHT = { DetailedSound = "PN_RLChicane", AccessibleSound = "PN_RLChicane" },
              _into = { DetailedSound = "PN_Into", AccessibleSound = "PN_Into" },
            }
            PaceNotesMerging = {
              PN_Into = { PN_RLChicane = "PN_IntoRLChicane", },
            }
            """;
        var catalog = Wrc7PacenoteCatalog.Parse(source);
        var decoder = new Wrc7LivePacenoteDecoder(catalog, new HashSet<string> { "into", "left_entry_chicane", "right_entry_chicane" });
        foreach (bool? mode in new bool?[] { false, true, null })
        {
            Assert.True(decoder.TryDecode(1, 1, resource, mode, out var result));
            Assert.Equal(expected, result!.Record.RawText);
        }
        var missing = new Wrc7LivePacenoteDecoder(catalog, new HashSet<string> { "into" });
        Assert.False(missing.TryDecode(1, 1, resource, null, out _));
    }
}

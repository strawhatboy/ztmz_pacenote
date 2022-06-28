
// How to get rbr root dir: HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Rallysimfans RBR\[InstallPath]
// RBR track name by track id: [rbrRoot]\Maps\Tracks.ini
//
// ==== Pacenotes ====
//      Use the latest pacenote for BTB track, use corresponding M,N,E,O pacenote for DLS track.
//      But check custom pacenote first
// ===================
//
// ---- Custom pacenote for track 
// [rbrRoot]\Plugins\NGPCarMenu.ini\[MyPacenotesPath] <disabled>|[PATH]
// [MyPacenotesPath]\[TrackName] [DateTime].ini
// ---- DLS pacenote for track
//  [rbrRoot]\Maps\[Track_ID]-[TrackName]\track-[TrackID]_[M|N|E|O].dls
//  or [rbrRoot]\Maps\track-[TrackID]_[M|N|E|O].dls
// ---- BTB pacenote for track (RX Tracks)
//  [rbrRoot]\RX_CONTENT\Tracks\[TrackName]\pacenotes[*].ini
//
// ---- DLS pacenote rules
//      1. check 0x38, if it's 1, then
//          pacenotecount: 0x5C, pacenotedata_addr: 0x7C (LE)
//      2. if 0x38 is not 1, then
//          pacenotecount: value before 00 00 00 00 1C 00 00 00
//          pacenotedata_addr: &pacenotecount + 0x20
//      3. read pacenotes
//          type:       0x00
//          modifier:   0x04
//          distance:   0x08

// ---- Get Track ID From Memory
//      1. read Int32 x from 7EA678
//      2. read Int32 y from x + 112
//      3. read Int32 z from y + 32
//      4. z is the Track ID.

using System;
using System.Collections.Generic;
using System.Windows.Media.Imaging;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.RBR
{
    public class RBR : IGame
    {
        public string WindowTitle => "";

        public string Name => "Richard Burns Rally - NGP6";

        public string Description => "The most classic rally simulation game";

        public BitmapImage Image { set => throw new NotImplementedException(); }

        public string Executable => throw new NotImplementedException();

        public IGamePacenoteReader GamePacenoteReader => throw new NotImplementedException();

        public IGameDataReader GameDataReader => throw new NotImplementedException();

        public bool IsRunning { get; set; }

        BitmapImage IGame.Image => throw new NotImplementedException();

        Dictionary<string, IGameConfig> IGame.GameConfigurations { get; } = new();
    }
}

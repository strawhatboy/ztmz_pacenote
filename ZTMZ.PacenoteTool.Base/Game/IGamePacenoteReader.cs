using System.Collections.Generic;

namespace ZTMZ.PacenoteTool.Base.Game
{
    public interface IGamePacenoteReader
    {
        IList<PacenoteRecord> ReadPacenoteRecord(string profile, IGame game, string track);
    }
}
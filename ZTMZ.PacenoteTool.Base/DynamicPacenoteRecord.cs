namespace ZTMZ.PacenoteTool.Base
{
    public class DynamicPacenoteRecord
    {
        public float Distance { set; get; }
        public string Pacenote { set; get; } = "";
        public string Modifier { set; get; } = "";
        public string RawVoiceCommand { set; get; }
        public string UnprocessedVoiceCommandText { set; get; }
    }
}

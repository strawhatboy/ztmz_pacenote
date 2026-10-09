using ZTMZ.PacenoteTool.KT;
using System.Text;

if (args.Length < 1) throw new ArgumentException("Expected telemetry.bin [game directory]");
using var reader = new BinaryReader(File.OpenRead(args[0]));
if (Encoding.ASCII.GetString(reader.ReadBytes(8)) != "WRCGUDP1") throw new InvalidDataException("Capture header");
long now=0;
var gameReader = new WrcGenerationsGameDataReader(()=>now);
gameReader.onGameStateChanged += e => Console.WriteLine($"State {e.LastGameState} -> {e.NewGameState}; lap={gameReader.CurrentGameData.LapTime}");
int count=0, rejected=0;
while (reader.BaseStream.Position + 12 <= reader.BaseStream.Length)
{
    now=reader.ReadInt64()/TimeSpan.TicksPerMillisecond;
    int size=reader.ReadInt32();
    if (size is < 0 or > 65535) throw new InvalidDataException("Packet length");
    if(reader.BaseStream.Position+size>reader.BaseStream.Length)break;
    var bytes=reader.ReadBytes(size);
    if (!WrcGenerationsTelemetry.TryDecode(bytes,out _)) rejected++;
    else gameReader.onNewUdpMessage([],bytes);
    count++;
}
Console.WriteLine($"Packets={count}, rejected={rejected}; final time={gameReader.CurrentGameData.LapTime}; RPM max/idle={gameReader.CurrentGameData.MaxRPM}/{gameReader.CurrentGameData.IdleRPM}");
if (args.Length > 1)
{
    var catalog = WrcGenerationsArchive.Load(args[1]);
    Console.WriteLine($"Catalog: {catalog.BlockCount} blocks / {catalog.AtomCount} atoms");
    if(!catalog.TryResolve("MALE/Block_5258",out var atoms)) throw new InvalidDataException("Known captured block missing");
    Console.WriteLine("Block_5258: " + string.Join('/',atoms));
    if(args.Length > 2)
    {
        var vocabulary=new HashSet<string>(StringComparer.Ordinal);
        if(Path.GetExtension(args[2])==".zdb")
        {
            using var connection=new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource=args[2],Mode=Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly }.ToString());
            connection.Open();using var command=connection.CreateCommand();command.CommandText="SELECT filename FROM pacenote_filenames";
            using var rows=command.ExecuteReader();while(rows.Read())vocabulary.Add(rows.GetString(0));
        }
        else
        {
            using var json=System.Text.Json.JsonDocument.Parse(File.ReadAllText(args[2]));
            vocabulary.UnionWith(json.RootElement.GetProperty("icons").EnumerateObject().Select(p=>p.Name));
        }
        var unknown=new SortedSet<string>();int complete=0,found=0;
        for(int i=0;i<10000;i++)
        {
            if(!catalog.TryResolve($"Block_{i:D4}",out var call))continue;
            found++;bool good=true;
            foreach(var atom in call)if(!WrcGenerationsTokenMapper.TryMap(atom,vocabulary,out _)){unknown.Add(atom);good=false;}
            if(good)complete++;
        }
        Console.WriteLine($"Fully mapped blocks against supplied vocabulary: {complete}/{found}; unresolved: {string.Join(", ",unknown)}");
    }
}
if(rejected!=0)Environment.ExitCode=1;

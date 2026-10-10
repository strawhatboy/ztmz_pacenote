using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace ZTMZ.PacenoteTool.KT;

// Format reference: https://github.com/Avery3R/WRCGModTools
// Reads selected definitions from the installed game; never writes game data.
public static class WrcGenerationsArchive
{
public static WrcGenerationsPacenoteCatalog Load(string game)
{
    var resources = ReadResources(game, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "PACENOTES.CFG", "PACENOTEBLOCKS.CFG" });
    return WrcGenerationsPacenoteCatalog.Parse(Encoding.UTF8.GetString(resources["PACENOTES.CFG"]), Encoding.UTF8.GetString(resources["PACENOTEBLOCKS.CFG"]));
}

internal static IReadOnlyDictionary<string, byte[]> ReadResources(string game, IReadOnlySet<string> requested)
{
var defaultKey = Encoding.ASCII.GetBytes("14F5FsyDFFUC4NVANPpYggyakreWkJfy");
using var rsa = RSA.Create();
rsa.ImportFromPem("""
-----BEGIN PUBLIC KEY-----
MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEAohx1scPAXQSAF4E7wuEz
ehIAiCiU9OMFGvCLoSmtQUOCQDPqY3bykOKBMqGJKQ7yaf55jiJHaW3lCZkWLOBO
46pAUOtoxVeQ9+4M4BmCUalTwWq/SsCc/JuEl6j+7DK1sGBAcjz/uyxvmVa85TtO
zDXEc2oDBhoNdg1AcMnwU7PQsdON/qiI7UIZ4JZ7QzoAklvA3GBdT93ln6UVy5U2
KWj8pCwMcVEJ5UOxdGWCebTvF7yxvPo+6AhkFUyrZ1lOWA6kgu8z3xzdnBet/fzf
+nHQZ5eT09ackoWjuGe6rFxAcEqVb80KqkPjqTIoKprkegl78yXeewraegjEXzZj
ywIDAQAB
-----END PUBLIC KEY-----
""");
var pub = rsa.ExportParameters(false);
var found = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
foreach (var file in Directory.EnumerateFiles(Path.Combine(game,"WIN32","PKG"),"*.PKG",SearchOption.AllDirectories))
{
    using var stream = File.OpenRead(file); using var reader = new BinaryReader(stream);
    var key = defaultKey; long headerOffset = 0;
    var header = Read(reader,48); Xor(header,key,0);
    if (Encoding.ASCII.GetString(header,0,4) != "PPKG")
    {
        stream.Position=0;var encoded=Read(reader,256);Xor(encoded,key,0);
        var decoded=BigInteger.ModPow(new BigInteger(encoded,true,true),new BigInteger(pub.Exponent!,true,true),new BigInteger(pub.Modulus!,true,true)).ToByteArray(true,true);
        var padded=new byte[256];decoded.CopyTo(padded,256-decoded.Length);
        if(padded[0]!=0 || padded[1]!=1)throw new InvalidDataException("RSA block padding");
        int p=2;while(p<padded.Length && padded[p]==255)++p;
        if(p<10 || p>=255 || padded[p]!=0)throw new InvalidDataException("RSA key padding");
        key=padded[(p+1)..];headerOffset=0x180;stream.Position=headerOffset;
        header=Read(reader,48);Xor(header,key,0);
    }
    if(Encoding.ASCII.GetString(header,0,4)!="PPKG")throw new InvalidDataException("PKG magic");
    uint indexSize=BitConverter.ToUInt32(header,12);
    if(indexSize>128*1024*1024 || indexSize>stream.Length-stream.Position)throw new InvalidDataException("Index size");
    var index=Read(reader,(int)indexSize);Xor(index,key,headerOffset+48);
    using var meta=new BinaryReader(new MemoryStream(index));
    while(meta.BaseStream.Position<meta.BaseStream.Length)
    {
        uint length=meta.ReadUInt32();if(length>32768)throw new InvalidDataException("Name size");
        var name=Encoding.UTF8.GetString(Read(meta,(int)length)).TrimEnd('\0');meta.ReadUInt32();
        ulong offset=meta.ReadUInt64();meta.ReadUInt64();ulong packed=meta.ReadUInt64(),unpacked=meta.ReadUInt64();uint compression=meta.ReadUInt32();Read(meta,48);
        var leaf = Path.GetFileName(name.Replace('\\', '/'));
        if(!requested.Contains(leaf)) continue;
        if(packed>64*1024*1024 || unpacked>64*1024*1024 || offset>(ulong)stream.Length || packed+8>(ulong)stream.Length-offset)throw new InvalidDataException("Payload bounds");
        stream.Position=(long)offset;if(Encoding.ASCII.GetString(Read(reader,4))!="PKGB")throw new InvalidDataException("Begin marker");
        var payload=Read(reader,(int)packed);if(Encoding.ASCII.GetString(Read(reader,4))!="PKGE")throw new InvalidDataException("End marker");
        var data=compression switch {0=>payload,2=>Lz4Frame(payload,(int)unpacked),_=>throw new InvalidDataException("Unsupported codec")};
        if(data.Length!=(int)unpacked)throw new InvalidDataException("Decoded size");
        if (!found.TryAdd(leaf, data)) throw new InvalidDataException("Duplicate game definition: " + leaf);
    }
    if (found.Count == requested.Count) break;

}
if (found.Count != requested.Count) throw new InvalidDataException("Missing game definitions: " + string.Join(", ", requested.Except(found.Keys)));
return found;
}
static byte[] Read(BinaryReader r,int n){var b=r.ReadBytes(n);if(b.Length!=n)throw new EndOfStreamException();return b;}
static void Xor(byte[] data,byte[] key,long offset){for(int i=0;i<data.Length;i++)data[i]^=key[(i+offset)%key.Length];}
static byte[] Lz4Frame(byte[] data,int expected)
{
    using var r=new BinaryReader(new MemoryStream(data));
    if(r.ReadUInt32()!=0x184D2204)throw new InvalidDataException("LZ4 frame magic");
    byte flags=r.ReadByte();r.ReadByte();if((flags>>6)!=1)throw new InvalidDataException("LZ4 version");
    if((flags&8)!=0 && r.ReadUInt64()!=(ulong)expected)throw new InvalidDataException("LZ4 content size");
    if((flags&1)!=0)throw new InvalidDataException("LZ4 dictionary not supported");r.ReadByte();
    var output=new byte[expected];int target=0;
    while(true){uint block=r.ReadUInt32();if(block==0)break;
        int size=checked((int)(block&0x7fffffff));if(size>4*1024*1024)throw new InvalidDataException("LZ4 block size");
        var bytes=Read(r,size);int source=0,start=target;
        if((block&0x80000000)!=0){bytes.CopyTo(output,target);target+=size;}
        else while(source<bytes.Length){int token=bytes[source++];int literals=Length(token>>4);
            if(literals>bytes.Length-source || literals>output.Length-target)throw new InvalidDataException("LZ4 literals");
            Array.Copy(bytes,source,output,target,literals);source+=literals;target+=literals;if(source==bytes.Length)break;
            if(source+2>bytes.Length)throw new InvalidDataException("LZ4 offset");int distance=bytes[source]|bytes[source+1]<<8;source+=2;
            int count=Length(token&15)+4;
            if(distance==0 || distance>target-((flags&32)!=0?start:0) || count>output.Length-target)throw new InvalidDataException("LZ4 match");
            for(int i=0;i<count;i++){output[target]=output[target-distance];++target;}
        }
        if((flags&16)!=0)r.ReadUInt32();
        int Length(int initial){if(initial!=15)return initial;int next;do{if(source>=bytes.Length)throw new InvalidDataException("LZ4 length");next=bytes[source++];initial=checked(initial+next);}while(next==255);return initial;}
    }
    if((flags&4)!=0)r.ReadUInt32();
    if(target!=expected || r.BaseStream.Position!=data.Length)throw new InvalidDataException("LZ4 frame length");
    return output;
}
}

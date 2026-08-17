namespace ZTMZ.PacenoteTool.Base;

public sealed record AudioOutputDeviceInfo(string Id, string Name, bool IsDefault)
{
    public override string ToString() => Name;
}

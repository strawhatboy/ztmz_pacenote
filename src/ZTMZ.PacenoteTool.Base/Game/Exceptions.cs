
using System;

namespace ZTMZ.PacenoteTool.Base.Game;

public class PortAlreadyInUseException : Exception
{
    public int Port { get; private set; }
    public PortAlreadyInUseException(string message, int port) : base(message)
    {
        Port = port;
    }
}


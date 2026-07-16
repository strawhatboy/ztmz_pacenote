
using System;
using System.Net.Sockets;

namespace ZTMZ.PacenoteTool.Base.Game;

public class PortAlreadyInUseException : Exception
{
    public int Port { get; private set; }
    public PortAlreadyInUseException(string message, int port) : base(message)
    {
        Port = port;
    }
}

public class PortAccessDeniedException : Exception
{
    public int Port { get; }
    public SocketError SocketError { get; }

    public PortAccessDeniedException(string message, int port, SocketError socketError, Exception innerException)
        : base(message, innerException)
    {
        Port = port;
        SocketError = socketError;
    }
}

using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading.Tasks;
using RiftWard.Sim;
using UnityEngine;

namespace RiftWard.Client
{
    /// Client side of the game-server connection. Messages are dispatched on the main thread from Poll().
    public sealed class NetClient
    {
        Connection conn;
        public bool Connected => conn != null && !conn.Closed;
        public int LocalId { get; private set; }
        public bool Welcomed { get; private set; }
        public event Action<Msg, System.IO.BinaryReader> Message;
        public event Action<string> Disconnected;
        bool reportedClose;

        public async void Connect(string host, int port, string token, Action<bool, string> done)
        {
            var tcp = new TcpClient();
            try
            {
                var connect = tcp.ConnectAsync(host, port);
                if (await Task.WhenAny(connect, Task.Delay(5000)) != connect || tcp.Client == null || !tcp.Connected)
                {
                    tcp.Close();
                    done(false, "無法連接遊戲伺服器");
                    return;
                }
                await connect;
            }
            catch (Exception e)
            {
                tcp.Close();
                done(false, "無法連接遊戲伺服器：" + e.Message);
                return;
            }
            conn = new Connection(0, tcp);
            reportedClose = false;
            Welcomed = false;
            conn.Send(Protocol.Frame(Msg.Hello, w => { w.Write(token); w.Write(Defs.Version); }));
            done(true, "");
        }

        public void Send(Msg type, Action<System.IO.BinaryWriter> body = null) => conn?.Send(Protocol.Frame(type, body));

        public void Close()
        {
            reportedClose = true;
            conn?.Close("client closed");
            conn = null;
            Welcomed = false;
        }

        public void Poll()
        {
            if (conn == null) return;
            while (conn != null && conn.Inbox.TryDequeue(out var msg))
            {
                using var r = Connection.Reader(msg.payload);
                if (msg.type == Msg.Welcome) { LocalId = r.ReadInt32(); Welcomed = true; }
                Message?.Invoke(msg.type, r);
            }
            if (conn != null && conn.Closed && !reportedClose)
            {
                reportedClose = true;
                string reason = conn.CloseReason;
                conn = null;
                Welcomed = false;
                Disconnected?.Invoke(reason);
            }
        }
    }
}

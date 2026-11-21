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
        /// Smoothed round-trip time in milliseconds (-1 until the first echo arrives).
        public int PingMs { get; private set; } = -1;
        /// Seconds since anything arrived from the server (0 while healthy).
        public float Silence => conn == null ? 0f : Time.realtimeSinceStartup - lastReceive;
        /// No byte from the server for this long while logged in: the link is treated as dead and closed,
        /// so the reconnect starts instead of the game freezing on a half-open socket.
        const float DeadAfter = 8f;
        float lastReceive, lastProbe;
        float smoothed = -1f;
        static readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();

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
            PingMs = -1; smoothed = -1f;
            lastReceive = lastProbe = Time.realtimeSinceStartup;
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
            PingMs = -1;
        }

        public void Poll()
        {
            if (conn == null) return;
            while (conn != null && conn.Inbox.TryDequeue(out var msg))
            {
                lastReceive = Time.realtimeSinceStartup;
                using var r = Connection.Reader(msg.payload);
                if (msg.type == Msg.NetPong)
                {
                    float rtt = Mathf.Max(0f, clock.ElapsedMilliseconds - r.ReadInt64());
                    smoothed = smoothed < 0 ? rtt : Mathf.Lerp(smoothed, rtt, 0.25f);
                    PingMs = Mathf.RoundToInt(smoothed);
                    continue;
                }
                if (msg.type == Msg.Welcome) { LocalId = r.ReadInt32(); Welcomed = true; }
                Message?.Invoke(msg.type, r);
            }
            if (conn != null && !conn.Closed && Welcomed)
            {
                float now = Time.realtimeSinceStartup;
                if (now - lastProbe >= 1f)
                {
                    lastProbe = now;
                    long stamp = clock.ElapsedMilliseconds;
                    Send(Msg.NetPing, w => w.Write(stamp));
                }
                if (now - lastReceive > DeadAfter) conn.Close("伺服器沒有回應");
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

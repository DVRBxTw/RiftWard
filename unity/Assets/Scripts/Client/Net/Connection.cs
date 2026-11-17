using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using RiftWard.Sim;

namespace RiftWard.Client
{
    /// A framed TCP connection. A background thread reads whole messages into a queue drained on the main thread.
    public sealed class Connection
    {
        public readonly int Id;
        readonly TcpClient tcp;
        readonly NetworkStream stream;
        readonly object sendLock = new object();
        public readonly ConcurrentQueue<(Msg type, byte[] payload)> Inbox = new ConcurrentQueue<(Msg, byte[])>();
        volatile bool closed;
        public bool Closed => closed;
        public string CloseReason = "";

        public Connection(int id, TcpClient tcp)
        {
            Id = id;
            this.tcp = tcp;
            tcp.NoDelay = true;
            tcp.ReceiveBufferSize = tcp.SendBufferSize = 1 << 18;
            stream = tcp.GetStream();
            new Thread(ReadLoop) { IsBackground = true, Name = "rw-net-" + id }.Start();
        }

        void ReadLoop()
        {
            var header = new byte[4];
            try
            {
                while (!closed)
                {
                    ReadExact(header, 4);
                    int length = BitConverter.ToInt32(header, 0);
                    if (length < 1 || length > Protocol.MaxMessage) throw new InvalidDataException("bad frame " + length);
                    var body = new byte[length];
                    ReadExact(body, length);
                    var payload = new byte[length - 1];
                    Buffer.BlockCopy(body, 1, payload, 0, length - 1);
                    Inbox.Enqueue(((Msg)body[0], payload));
                }
            }
            catch (Exception e) { Close(e.Message); }
        }

        void ReadExact(byte[] buffer, int count)
        {
            int read = 0;
            while (read < count)
            {
                int n = stream.Read(buffer, read, count - read);
                if (n <= 0) throw new EndOfStreamException("disconnected");
                read += n;
            }
        }

        public void Send(byte[] frame)
        {
            if (closed) return;
            try { lock (sendLock) stream.Write(frame, 0, frame.Length); }
            catch (Exception e) { Close(e.Message); }
        }

        public void Close(string reason = "")
        {
            if (closed) return;
            closed = true;
            CloseReason = reason;
            try { tcp.Close(); } catch { }
        }

        public static BinaryReader Reader(byte[] payload) => new BinaryReader(new MemoryStream(payload));
    }
}

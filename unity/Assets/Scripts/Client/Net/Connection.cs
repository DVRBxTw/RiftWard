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
        /// Player id on the server. Reassigned when a dropped player reconnects and takes back their old seat.
        public int Id;
        readonly TcpClient tcp;
        readonly NetworkStream stream;
        readonly object sendLock = new object();
        readonly ConcurrentQueue<byte[]> outgoing = new ConcurrentQueue<byte[]>();
        readonly AutoResetEvent sendReady = new AutoResetEvent(false);
        const int MaxQueuedBytes = 4 << 20;
        int queuedBytes;
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
            stream.WriteTimeout = 10000;
            new Thread(ReadLoop) { IsBackground = true, Name = "rw-net-" + id }.Start();
            new Thread(WriteLoop) { IsBackground = true, Name = "rw-send-" + id }.Start();
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
            lock (sendLock)
            {
                if (closed) return;
                if (Interlocked.Add(ref queuedBytes, frame.Length) > MaxQueuedBytes)
                {
                    Close("send queue full");
                    return;
                }
                outgoing.Enqueue(frame);
                sendReady.Set();
            }
        }

        // Never block Unity's main thread on a member who is loading or has a slow connection.
        // Frames remain FIFO; a stalled peer is closed without freezing the other rooms or players.
        void WriteLoop()
        {
            try
            {
                while (!closed)
                {
                    while (!closed && outgoing.TryDequeue(out var frame))
                    {
                        stream.Write(frame, 0, frame.Length);
                        Interlocked.Add(ref queuedBytes, -frame.Length);
                    }
                    if (!closed) sendReady.WaitOne();
                }
            }
            catch (Exception e) { Close(e.Message); }
            finally { while (outgoing.TryDequeue(out _)) { } }
        }

        public void Close(string reason = "")
        {
            if (closed) return;
            closed = true;
            CloseReason = reason;
            sendReady.Set();
            try { tcp.Close(); } catch { }
        }

        public static BinaryReader Reader(byte[] payload) => new BinaryReader(new MemoryStream(payload));
    }
}

using System.Net.Sockets;

namespace SignalForward.TCP
{
    public class SimpleTcpClient : IDisposable
    {
        private Task? _rxTask;
        private TcpClient? _client;
        private static int _timeouts = 0;
        private static readonly object CountLock = new();
        private CancellationTokenSource? _cts;

        public event EventHandler<Message>? DataReceived;

        public byte Delimiter { get; set; }

        /// <summary>
        /// 编码格式
        /// </summary>
        public System.Text.Encoding StringEncoder { get; set; }

        /// <summary>
        /// 停止消息
        /// </summary>
        internal bool QueueStop { get; set; }

        /// <summary>
        /// 循环读取间隔时间
        /// </summary>
        internal int ReadLoopIntervalMs { get; set; }

        /// <summary>
        /// 自动去除空白字符串
        /// </summary>
        public bool AutoTrimStrings { get; set; }

        /// <summary>
        /// ip地址
        /// </summary>
        private string? HostNameOrIpAddress { get; set; }

        /// <summary>
        /// 端口号
        /// </summary>
        private int Port { get; set; }

        public SimpleTcpClient()
        {
            StringEncoder = System.Text.Encoding.UTF8;
            ReadLoopIntervalMs = 10;
            Delimiter = 0x13;
        }

        /// <summary>
        /// 连接
        /// </summary>
        /// <param name="hostNameOrIpAddress">ip地址</param>
        /// <param name="port">端口号</param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        public SimpleTcpClient Connect(string hostNameOrIpAddress, int port)
        {
            if (string.IsNullOrEmpty(hostNameOrIpAddress))
            {
                throw new ArgumentNullException("hostNameOrIpAddress");
            }
            _cts = new CancellationTokenSource();
            _client = new TcpClient();
            this.HostNameOrIpAddress = hostNameOrIpAddress;
            this.Port = port;
            _client.Connect(hostNameOrIpAddress, port);

            StartRxThread();
            return this;
        }

        private void StartRxThread()
        {
            if (_rxTask != null) { return; }
            if (_cts == null) { return; }
            _rxTask = Task.Factory.StartNew(() => { ListenerLoop(); }, _cts.Token);
        }

        /// <summary>
        /// 关闭
        /// </summary>
        /// <returns></returns>
        public SimpleTcpClient Disconnect()
        {
            if (_client == null) { return this; }
            _client.Close();
            _client = null;
            _cts?.Cancel();
            _cts?.Dispose();
            return this;
        }

        public TcpClient? TcpClient => _client;

        private void ListenerLoop()
        {
            while (!QueueStop)
            {
                try
                {
                    if (_cts != null && _cts.IsCancellationRequested)
                    {
                        return;
                    }
                    RunLoopStep();
                }
                catch
                {
                    // ignored
                }

                Thread.Sleep(ReadLoopIntervalMs);
            }
            _rxTask = null;
        }

        private void RunLoopStep()
        {
            if (_client == null) { return; }

            if (_client.Connected == false)
            {
                //自动重连
                lock (CountLock) { _timeouts++; }

                if (_timeouts <= 30) return;
                if (HostNameOrIpAddress != null) _client.Connect(HostNameOrIpAddress, Port);
                if (_client.Connected) { _timeouts = 0; }
                return;
            }

            var delimiter = Delimiter;
            var c = _client;

            var bytesAvailable = c.Available;
            if (bytesAvailable == 0)
            {
                Thread.Sleep(10);
                return;
            }

            var bytesReceived = new List<byte>();

            while (c.Available > 0 && c.Connected)
            {
                byte[] nextByte = new byte[1];
                c.Client.Receive(nextByte, 0, 1, SocketFlags.None);
                bytesReceived.AddRange(nextByte);
            }

            if (bytesReceived.Count > 0)
            {
                NotifyEndTransmissionRx(c, bytesReceived.ToArray());
            }
        }

        private void NotifyEndTransmissionRx(TcpClient client, byte[] msg)
        {
            if (DataReceived == null) return;
            var m = new Message(msg, client, StringEncoder, Delimiter, AutoTrimStrings);
            DataReceived(this, m);
        }

        public void Write(byte[] data)
        {
            if (_client == null) { throw new Exception("Cannot send data to a null TcpClient (check to see if Connect was called)"); }
            _client.GetStream().Write(data, 0, data.Length);
        }

        public void WriteAsync(byte[] data)
        {
            if (_client == null) { throw new Exception("Cannot send data to a null TcpClient (check to see if Connect was called)"); }
            _client.GetStream().WriteAsync(data, 0, data.Length);
        }

        public void Write(string? data)
        {
            if (data == null) { return; }
            Write(StringEncoder.GetBytes(data));
        }

        public void WriteLine(string? data)
        {
            if (string.IsNullOrEmpty(data)) { return; }
            if (data.LastOrDefault() != Delimiter)
            {
                Write(data + StringEncoder.GetString(new[] { Delimiter }));
            }
            else
            {
                Write(data);
            }
        }

        #region IDisposable Support

        private bool _disposedValue = false; // To detect redundant calls

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    // TODO: dispose managed state (managed objects).
                }

                // TODO: free unmanaged resources (unmanaged objects) and override a finalizer below.
                // TODO: set large fields to null.
                QueueStop = true;
                if (_client != null)
                {
                    try
                    {
                        _client.Close();
                    }
                    catch
                    {
                        // ignored
                    }

                    _client = null;
                }

                _disposedValue = true;
            }
        }

        // TODO: override a finalizer only if Dispose(bool disposing) above has code to free unmanaged resources.
        // ~SimpleTcpClient() {
        //   // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
        //   Dispose(false);
        // }

        // This code added to correctly implement the disposable pattern.
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
            // TODO: uncomment the following line if the finalizer is overridden above.
            // GC.SuppressFinalize(this);
        }

        #endregion IDisposable Support
    }
}
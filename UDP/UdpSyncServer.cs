using log4net;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace SignalForward.UDP
{
    /// <summary>
    ///  同步的UDP Server
    /// </summary>
    public class UdpSyncServer : IDisposable
    {
        /// <summary>
        /// 服务器使用的异步UdpClient
        /// </summary>
        public UdpClient Server;

        private bool _disposed = false;

        public int ReceiveBufferSize { get; set; } = 128;

        /// <summary>
        /// 服务器是否正在运行
        /// </summary>
        public bool IsRunning { get; private set; }

        /// <summary>
        /// 监听的IP地址
        /// </summary>
        public IPAddress Address { get; private set; }

        /// <summary>
        /// 监听的端口
        /// </summary>
        public int Port { get; private set; }

        /// <summary>
        /// 通信使用的编码
        /// </summary>
        public Encoding Encoding { get; set; }

        public string Name { get; set; }

        private AsyncUDPState? _udpReceiveState = default;

        private ILog? _logger;

        /// <summary>
        /// 同步UdpClient UDP服务器
        /// </summary>
        /// <param name="localIPAddress">监听的IP地址</param>
        /// <param name="listenPort">监听的端口</param>
        /// <param name="maxClient">最大客户端数量</param>
        public UdpSyncServer(IPAddress localIpAddress, int listenPort, ILog logger)
        {
            this.Address = localIpAddress;
            this.Port = listenPort;
            this.Encoding = Encoding.Default;
            _logger = logger;
            //_clients = new List<AsyncUDPSocketState>();
            Server = new UdpClient(new IPEndPoint(this.Address, this.Port));
            Server.Client.ReceiveBufferSize = ReceiveBufferSize;
        }

        /// <summary>
        /// 启动服务器
        /// </summary>
        /// <returns>异步TCP服务器</returns>
        public void Start()
        {
            if (IsRunning) return;
            IsRunning = true;
            Server.EnableBroadcast = false;
            Server.BeginReceive(ReceiveDataAsync, _udpReceiveState);
        }

        /// <summary>
        /// 接收数据的方法
        /// </summary>
        /// <param name="ar"></param>
        private void ReceiveDataAsync(IAsyncResult ar)
        {
            AsyncUDPState? udpState = ar.AsyncState as AsyncUDPState;
            IPEndPoint? remote = default;
            byte[]? buffer = null;
            try
            {
                if (!ar.IsCompleted) return;
                buffer = Server?.EndReceive(ar, ref remote);

                //触发数据收到事件
                RaiseDataReceived(buffer);
            }
            catch (Exception exception)
            {
                // ignored
                _logger?.Error(exception.Message, exception);
            }
            finally
            {
                lock (this)
                {
                    if (IsRunning && Server != null)
                    {
                        _udpReceiveState = new AsyncUDPState();
                        Server.BeginReceive(ReceiveDataAsync, _udpReceiveState);
                    }
                }
            }
        }

        /// <summary>
        /// 接收到数据事件
        /// </summary>
        public event EventHandler<byte[]> DataReceived;

        private void RaiseDataReceived(byte[] buffer)
        {
            DataReceived?.Invoke(this, buffer);
        }

        /// <summary>
        /// 停止服务器
        /// </summary>
        public void Stop()
        {
            if (!IsRunning) return;
            IsRunning = false;
            Server.Close();
        }

        /// <summary>
        /// 同步发送数据
        /// </summary>
        /// <param name="remote"></param>
        /// <param name="bits"></param>
        public void Send(IPEndPoint remote, byte[] bits)
        {
            try
            {
                Server.Send(bits, bits.Length, remote);
            }
            catch (Exception e)
            {
                _logger?.Error("同步发送消息失败:" + e.Message, e);
            }
        }

        /// <summary>
        /// 异步发送数据
        /// </summary>
        /// <param name="remote"></param>
        /// <param name="bits"></param>
        public void SendAsync(IPEndPoint remote, byte[] bits)
        {
            try
            {
                Server.BeginSend(bits, bits.Length, remote, new AsyncCallback(SendCallback), _udpReceiveState);
            }
            catch (Exception e)
            {
                _logger?.Error("开始异步发送消息失败:" + e.Message, e);
            }
        }

        private void SendCallback(IAsyncResult ar)
        {
            //AsyncUDPState state = ar.AsyncState as AsyncUDPState;
            if (!ar.IsCompleted) return;
            try
            {
                Server.EndSend(ar);
                //消息发送完毕事件
                // Logger.Info("fa");
            }
            catch (Exception e)
            {
                _logger?.Error("结束异步发送消息失败:" + e.Message, e);
            }
        }

        #region 释放

        /// <summary>
        /// Performs application-defined tasks associated with freeing,
        /// releasing, or resetting unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Releases unmanaged and - optionally - managed resources
        /// </summary>
        /// <param name="disposing"><c>true</c> to release
        /// both managed and unmanaged resources; <c>false</c>
        /// to release only unmanaged resources.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (this._disposed) return;
            if (disposing)
            {
                try
                {
                    Stop();
                    if (Server != null)
                    {
                        Server = null;
                    }
                }
                catch (SocketException e)
                {
                }
            }
            _disposed = true;
        }

        #endregion 释放
    }

    public class WhCurrentQueue<T>
    {
        private ConcurrentQueue<T> _queue = new ConcurrentQueue<T>();
        private readonly SemaphoreSlim semaphore = new SemaphoreSlim(0, 1000);
        public readonly string Name;
        public ILog Logger;
        public int Count => _queue.Count;

        public WhCurrentQueue(string name, ILog logger)
        {
            this.Name = name;
            Logger = logger;
        }

        /// <summary>
        /// 队列出队
        /// </summary>
        /// <param name="obj"></param>
        public void Dequeue(out T obj)
        {
            semaphore.Wait();
            _queue.TryDequeue(out obj);
            var count = _queue.Count;
            Logger?.Info($"{$"[{Name}]",-10}{"队列出队成功！",-18}剩余 {count}");
        }

        /// <summary>
        /// 队列入队
        /// </summary>
        /// <param name="obj"></param>
        public void Enqueue(T obj)
        {
            var count = _queue.Count + 1;
            _queue.Enqueue(obj);
            semaphore.Release(1);
            Logger?.Info($"{$"[{Name}]",-10}{"队列入队成功！",-18}剩余 {count}");
        }

        public void Clear()
        {
            while (_queue.TryDequeue(out _))
            {

            }
        }
    }

    public class AsyncUDPState
    {
        // Client   socket.
        public UdpClient udpClient = null;

        public IPEndPoint remote;
    }
}
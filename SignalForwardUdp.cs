using SignalForward.UDP;
using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SignalForward
{
    public partial class SignalForwardUdp : Form
    {
        #region 变量

        public log4net.ILog? Logger;

        /// <summary>
        /// 远程Udp对象
        /// </summary>
        private UdpSyncServer? _remoteUdp;

        /// <summary>
        /// PLC
        /// </summary>
        private IPEndPoint? _plcIpEndPoint;

        /// <summary>
        /// AOI1
        /// </summary>
        private IPEndPoint? _aoi1PortEndPoint;

        /// <summary>
        /// AOI2
        /// </summary>
        private IPEndPoint? _aoi2PortEndPoint;

        /// <summary>
        /// 本地连接
        /// </summary>
        private UdpSyncServer? _localUdp;

        /// <summary>
        /// 本地连接1
        /// </summary>
        private UdpSyncServer? _localUdp1;

        /// <summary>
        /// 通讯信号
        /// </summary>
        public WhCurrentQueue<PendingData>? RemoteQueue;

        /// <summary>
        /// 待删除的信号
        /// </summary>
        public WhCurrentQueue<PendingData>? RemoveQueue;

        /// <summary>
        /// AOI1发送的消息
        /// </summary>
        public ConcurrentDictionary<byte[], byte[]> Aoi1Message = new();

        /// <summary>
        /// AOI2发送的消息
        /// </summary>
        public ConcurrentDictionary<byte[], byte[]> Aoi2Message = new();

        /// <summary>
        /// 自旋锁
        /// </summary>
        public SpinLock spinLock = new();

        /// <summary>
        /// 自旋锁1
        /// </summary>
        public SpinLock spinLock1 = new();

        /// <summary>
        ///  令牌
        /// </summary>
        private CancellationTokenSource? _tokenSource;

        /// <summary>
        /// 令牌2
        /// </summary>
        private CancellationTokenSource? _tokenSource1;

        /// <summary>
        /// 令牌3
        /// </summary>
        private CancellationTokenSource? _tokenSource2 = new();

        private static int _timeout = 600;

        private static int _timeout1 = 2;

        private byte[] _moRen1 = new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        private byte[] _moRen = new byte[] { 48, 48, 48, 48, 48, 48, 48, 48, 48, 48 };

        public string passWord = "123456";

        public DateTime CurTime = default;

        public DateTime BeforeTime = default;

        public bool IsUse = false;

        /// <summary>
        /// 外观
        /// </summary>
        public byte WaiGuan = 2;

        /// <summary>
        /// 颜色
        /// </summary>
        public byte Color = 3;

        public int StartIndex = 80;

        public int Length = 20;

        public int StartIndex1 = 100;

        public int Length1 = 20;

        public bool OneTakePhoto = false;

        #endregion 变量

        public SignalForwardUdp()
        {
            Logger = log4net.LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
            RemoteQueue = new WhCurrentQueue<PendingData>("等待发送结果", Logger);
            RemoveQueue = new WhCurrentQueue<PendingData>("等待删除结果", Logger);
            InitializeComponent();

            SetAllControlsEnabled(false);

            InitParam();
            CallOnClick(RemoteBnt);
            CallOnClick(button1);
            CallOnClick(button2);
        }

        #region 辅助方法

        /// <summary>
        /// 设置所有输入控件的启用状态
        /// </summary>
        private void SetAllControlsEnabled(bool enabled)
        {
            PlcIp.Enabled = enabled;
            PlcPort.Enabled = enabled;
            Plc_oneIp.Enabled = enabled;
            Plc_onePort.Enabled = enabled;

            Aoi1_oneIp.Enabled = enabled;
            Aoi_onePort.Enabled = enabled;
            Aoi1Ip.Enabled = enabled;
            Aoi1Port.Enabled = enabled;

            Aoi2_oneIp.Enabled = enabled;
            Aoi2_onePort.Enabled = enabled;
            Aoi2Ip.Enabled = enabled;
            Aoi2Port.Enabled = enabled;
            numericUpDown1.Enabled = enabled;
            numericUpDown2.Enabled = enabled;
        }

        /// <summary>
        /// 触发按钮的点击事件
        /// </summary>
        private void CallOnClick(Button btn)
        {
            var m = typeof(Button).GetMethod("OnClick", BindingFlags.NonPublic | BindingFlags.Instance);
            m.Invoke(btn, new object[] { EventArgs.Empty });
        }

        /// <summary>
        /// 在消息字典中查找匹配的消息
        /// </summary>
        private static KeyValuePair<byte[], byte[]> FindMessage(ConcurrentDictionary<byte[], byte[]> dict, byte operation, byte[] destination)
        {
            return dict.Where(item =>
                item.Key[2] == operation && item.Value.Skip(34).Take(10).ToArray().SequenceEqual(destination)
            ).FirstOrDefault();
        }

        /// <summary>
        /// 判断KeyValuePair是否有效
        /// </summary>
        private static bool IsValidKvp(KeyValuePair<byte[], byte[]> kvp)
        {
            return !kvp.Equals(default(KeyValuePair<byte[], byte[]>));
        }

        /// <summary>
        /// 创建响应字节数组副本
        /// </summary>
        private static byte[] CreateResponseCopy(byte[] original, byte b1, byte b2, byte b3)
        {
            var re = new byte[original.Length];
            Array.Copy(original, re, original.Length);
            re[1] = b1;
            re[2] = b2;
            re[3] = b3;
            return re;
        }

        /// <summary>
        /// 计算从指定时间到现在经过的毫秒数
        /// </summary>
        private static long GetElapsedMs(DateTime beforeDt)
        {
            return DateTime.Now.Subtract(beforeDt).Ticks / 10000;
        }

        /// <summary>
        /// 处理AOI数据接收
        /// </summary>
        private void HandleAoiDataReceived(byte[] bytes, string aoiName, ConcurrentDictionary<byte[], byte[]> aoiMessages)
        {
            Logger?.Info($"{label19.Text}-{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}接收{aoiName}消息:");

            var jiuxu = bytes[1];
            var caozhuo = bytes[2];
            var liushuihao = bytes.Skip(34).Take(10);

            if (jiuxu == 1 && caozhuo == 3)
            {
                if (_remoteUdp != null || _plcIpEndPoint != null)
                    _remoteUdp?.Send(_plcIpEndPoint, bytes);
                Logger?.Info("发送启动信号");
                Logger?.Info(bytes);
            }
            else if (jiuxu == 0 && caozhuo == 3)
            {
                if (_remoteUdp != null || _plcIpEndPoint != null)
                    _remoteUdp?.SendAsync(_plcIpEndPoint, bytes);
                Logger?.Info("发送暂停信号");
                Logger?.Info(bytes);
            }
            else if ((caozhuo == 1 || caozhuo == 2 || caozhuo == 0) && (liushuihao.SequenceEqual(_moRen1)) || liushuihao.SequenceEqual(_moRen))
            {
                Logger?.Info(bytes);
            }
            else if (bytes[0] == 0 && jiuxu == 1 && caozhuo == 0)
            {
                Logger?.Info(bytes);
            }
            else
            {
                Logger?.Info(bytes);
                aoiMessages.TryAdd(bytes, bytes);
                Logger?.Info($"{label19.Text}-{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}添加到{aoiName}消息列表:");
            }
        }

        public byte[] GetBytes()
        {
            int seed = DateTime.Now.Millisecond;
            Random random = new Random(seed);
            byte[] byteArray = new byte[10];
            random.NextBytes(byteArray);
            return byteArray;
        }

        #endregion 辅助方法

        private void RemoteBnt_Click_1(object sender, EventArgs e)
        {
            try
            {
                if (Logger == null) return;

                RemoteBnt.Enabled = false;
                button3.Enabled = true;
                _plcIpEndPoint = new IPEndPoint(IPAddress.Parse(PlcIp.Text.Trim()), int.Parse(PlcPort.Text.Trim()));

                _remoteUdp = new UdpSyncServer(IPAddress.Parse(Plc_oneIp.Text.Trim()), int.Parse(Plc_onePort.Text.Trim()), Logger);

                BeforeTime = DateTime.Now;
                _timeout1 = (int)numericUpDown2.Value;
                _remoteUdp.DataReceived += (object? sender, byte[] dataBytes) =>
                {
                    try
                    {
                        Logger?.Info($"{label19.Text}-{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}接收自动化消息:");
                        Logger?.Info(dataBytes);
                        if (_localUdp != null || _localUdp1 != null)
                        {
                            CurTime = DateTime.Now;
                            if (CurTime.Subtract(BeforeTime).TotalSeconds > _timeout1)
                            {
                                Aoi1Message?.Clear();
                                Aoi2Message?.Clear();
                                RemoteQueue?.Clear();
                                Logger?.Info($"{label19.Text}-{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}清除通讯数据缓存");
                            }
                            BeforeTime = CurTime;
                            int xinhao = dataBytes[66];
                            var shifoupaizhao = dataBytes[3];
                            switch (xinhao)
                            {
                                case 1:
                                    if (shifoupaizhao == 1)
                                    {
                                        var newBytes = new byte[dataBytes.Length];
                                        newBytes[3] = 1;
                                        Array.Copy(dataBytes, 34, newBytes, 34, 10);
                                        Array.Copy(dataBytes, StartIndex, newBytes, 44, Length);

                                        var newBytes1 = new byte[128];
                                        newBytes1[3] = 1;
                                        if (IsUse) newBytes1[20] = 1;
                                        var id = GetBytes();
                                        Array.Copy(id, 0, newBytes1, 34, 10);

                                        if (OneTakePhoto)
                                        {
                                            if (_aoi1PortEndPoint != null) _localUdp?.Send(_aoi1PortEndPoint, newBytes);
                                            Logger?.Info("O->AOI1");
                                            RemoteQueue?.Enqueue(new PendingData { Type = 1, ty = 1, Bytes1 = newBytes, Bytes2 = newBytes1, BytesOriginal = dataBytes });
                                        }
                                        else
                                        {
                                            if (_aoi2PortEndPoint != null) _localUdp1?.Send(_aoi2PortEndPoint, newBytes1);
                                            Logger?.Info("O->AOI2");
                                            if (_aoi1PortEndPoint != null) _localUdp?.Send(_aoi1PortEndPoint, newBytes);
                                            Logger?.Info("O->AOI1");
                                            RemoteQueue?.Enqueue(new PendingData { Type = 3, ty = 1, Bytes1 = newBytes, Bytes2 = newBytes1, BytesOriginal = dataBytes });
                                        }
                                    }
                                    break;

                                case 2:
                                    if (shifoupaizhao == 1)
                                    {
                                        var newBytes = new byte[dataBytes.Length];
                                        newBytes[3] = 1;
                                        Array.Copy(dataBytes, 44, newBytes, 34, 10);
                                        Array.Copy(dataBytes, StartIndex1, newBytes, 44, Length1);

                                        var newBytes1 = new byte[128];
                                        newBytes1[3] = 1;
                                        if (IsUse) newBytes1[20] = 1;
                                        var id = GetBytes();
                                        Array.Copy(id, 0, newBytes1, 34, 10);

                                        if (OneTakePhoto)
                                        {
                                            if (_aoi2PortEndPoint != null) _localUdp1?.Send(_aoi2PortEndPoint, newBytes);
                                            Logger?.Info("O->AOI2");
                                            RemoteQueue?.Enqueue(new PendingData { Type = 2, ty = 2, Bytes1 = newBytes1, Bytes2 = newBytes, BytesOriginal = dataBytes });
                                        }
                                        else
                                        {
                                            if (_aoi1PortEndPoint != null) _localUdp?.Send(_aoi1PortEndPoint, newBytes1);
                                            Logger?.Info("O->AOI1");
                                            if (_aoi2PortEndPoint != null) _localUdp1?.Send(_aoi2PortEndPoint, newBytes);
                                            Logger?.Info("O->AOI2");
                                            RemoteQueue?.Enqueue(new PendingData { Type = 3, ty = 2, Bytes1 = newBytes1, Bytes2 = newBytes, BytesOriginal = dataBytes });
                                        }
                                    }
                                    break;

                                case 3:
                                    if (shifoupaizhao == 1)
                                    {
                                        var newBytes = new byte[dataBytes.Length];
                                        newBytes[3] = 1;
                                        newBytes[5] = 1;
                                        Array.Copy(dataBytes, 34, newBytes, 34, 10);
                                        Array.Copy(dataBytes, StartIndex, newBytes, 44, Length);

                                        var newBytes1 = new byte[128];
                                        newBytes1[3] = 1;
                                        Array.Copy(dataBytes, 44, newBytes1, 34, 10);
                                        Array.Copy(dataBytes, StartIndex1, newBytes1, 44, Length1);

                                        if (_aoi1PortEndPoint != null) _localUdp?.Send(_aoi1PortEndPoint, newBytes);
                                        Logger?.Info("O->AOI1");
                                        if (_aoi2PortEndPoint != null) _localUdp1?.Send(_aoi2PortEndPoint, newBytes1);
                                        Logger?.Info("O->AOI2");
                                        RemoteQueue?.Enqueue(new PendingData { Type = 3, ty = 3, Bytes1 = newBytes, Bytes2 = newBytes1, BytesOriginal = dataBytes });
                                    }
                                    break;

                                default:
                                    Logger?.Info($"{label19.Text}-{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}半片标识为0:");
                                    Logger?.Info(dataBytes);
                                    Logger?.Info("----------------------------------------------------");
                                    break;
                            }
                        }
                        else
                        {
                            Logger?.Info("本地连接AOI断开");
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger?.Error(ex.Message);
                    }
                };
                _remoteUdp.Start();
            }
            catch (Exception exception)
            {
                RemoteBnt.Enabled = true;
                button3.Enabled = false;
                if (_remoteUdp != null)
                {
                    _remoteUdp.Stop();
                    _remoteUdp.Dispose();
                    _remoteUdp = null;
                }

                MessageBox.Show(exception.Message);
            }
        }

        private void button1_Click(object sender, EventArgs e)
        {
            try
            {
                if (Logger == null) return;
                button1.Enabled = false;
                button4.Enabled = true;
                _aoi1PortEndPoint =
                    new IPEndPoint(IPAddress.Parse(Aoi1Ip.Text.Trim()), int.Parse(Aoi1Port.Text.Trim()));
                _localUdp = new UdpSyncServer(IPAddress.Parse(Aoi1_oneIp.Text.Trim()),
                    int.Parse(Aoi_onePort.Text.Trim()), Logger);
                _localUdp.DataReceived += (o, bytes) => HandleAoiDataReceived(bytes, "AOI1", Aoi1Message);
                _localUdp.Start();
            }
            catch (Exception exception)
            {
                button1.Enabled = true;
                button4.Enabled = false;
                if (_localUdp != null)
                {
                    _localUdp.Stop();
                    _localUdp.Dispose();
                    _localUdp = null;
                }

                MessageBox.Show(exception.Message);
            }
        }

        private void button2_Click_1(object sender, EventArgs e)
        {
            try
            {
                SaveJsonData();
                if (CP.Checked)
                {
                    CB.Enabled = false;
                }
                else if (CP.Checked == false && CB.Checked == false)
                {
                    MessageBox.Show("请选择通讯模式！");
                    return;
                }
                else
                {
                    CP.Enabled = false;
                }
                SetAllControlsEnabled(false);
                if (Logger == null) return;
                button2.Enabled = false;
                button5.Enabled = true;
                _aoi2PortEndPoint =
                    new IPEndPoint(IPAddress.Parse(Aoi2Ip.Text.Trim()), int.Parse(Aoi2Port.Text.Trim()));
                _localUdp1 = new UdpSyncServer(IPAddress.Parse(Aoi2_oneIp.Text.Trim()),
                    int.Parse(Aoi2_onePort.Text.Trim()), Logger);
                _localUdp1.DataReceived += (o, bytes) => HandleAoiDataReceived(bytes, "AOI2", Aoi2Message);
                _localUdp1.Start();
                _timeout = ((int)numericUpDown1.Value);
            }
            catch (Exception exception)
            {
                button2.Enabled = true;
                button5.Enabled = false;
                if (_localUdp1 != null)
                {
                    _localUdp1.Stop();
                    _localUdp1.Dispose();
                    _localUdp1 = null;
                }

                MessageBox.Show(exception.Message);
            }
        }

        #region 成品

        /// <summary>
        /// 转发AOI发送的消息给PLC
        /// </summary>
        private void Transmit()
        {
            while (true)
            {
                _tokenSource1?.Token.ThrowIfCancellationRequested();
                try
                {
                    long timeOut;
                    DateTime beforeDt = default;
                    var inPhoto = false;
                    var photoCompleted = true;
                    var complete = true;
                    PendingData value = default;
                    if (RemoteQueue == null || _plcIpEndPoint == null || RemoveQueue == null || _localUdp == null || _localUdp1 == null)
                    {
                        continue;
                    }
                    RemoteQueue.Dequeue(out value);
                    Logger?.Info(value);
                    switch (value.Type)
                    {
                        case 1:
                            var destination1 = value.Bytes1.Skip(34).Take(10).ToArray();
                            beforeDt = DateTime.Now;

                            while ((inPhoto || photoCompleted || complete) && (timeOut = GetElapsedMs(beforeDt)) < _timeout)
                            {
                                var b = FindMessage(Aoi1Message, 1, destination1);
                                if (IsValidKvp(b))
                                {
                                    var re = CreateResponseCopy(value.BytesOriginal, 1, 1, 0);
                                    _remoteUdp?.Send(_plcIpEndPoint, re);
                                    Logger?.Info($"{label19.Text}-{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}拍照完成O->PLC:");
                                    photoCompleted = false;
                                    Aoi1Message.TryRemove(b.Key, out _);
                                }

                                var c = FindMessage(Aoi1Message, 2, destination1);
                                if (IsValidKvp(c))
                                {
                                    var re = CreateResponseCopy(value.BytesOriginal, 1, 2, 0);
                                    re[9] = c.Value[9];
                                    re[10] = c.Value[10];
                                    re[89] = c.Value[11];
                                    re[90] = c.Value[90];
                                    _remoteUdp?.Send(_plcIpEndPoint, re);
                                    Logger?.Info($"{label19.Text}-{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}发送结果O->PLC:{c.Value[9]},{c.Value[10]}");
                                    complete = false;
                                    Aoi1Message.TryRemove(c.Key, out _);
                                }
                            }
                            break;

                        case 2:
                            var destination2 = value.Bytes2.Skip(34).Take(10).ToArray();
                            beforeDt = DateTime.Now;
                            while ((inPhoto || photoCompleted || complete) && (timeOut = GetElapsedMs(beforeDt)) < _timeout)
                            {
                                var b = FindMessage(Aoi2Message, 1, destination2);
                                if (IsValidKvp(b))
                                {
                                    var re = CreateResponseCopy(value.BytesOriginal, 1, 1, 0);
                                    _remoteUdp?.Send(_plcIpEndPoint, re);
                                    Logger?.Info($"{label19.Text}-{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}拍照完成O->PLC:");
                                    photoCompleted = false;
                                    Aoi2Message.TryRemove(b.Key, out _);
                                }

                                var c = FindMessage(Aoi2Message, 2, destination2);
                                if (IsValidKvp(c))
                                {
                                    var re = CreateResponseCopy(value.BytesOriginal, 1, 2, 0);
                                    re[11] = c.Value[9];
                                    re[12] = c.Value[10];
                                    re[89] = c.Value[11];
                                    re[90] = c.Value[90];
                                    _remoteUdp?.Send(_plcIpEndPoint, re);
                                    Logger?.Info($"{label19.Text}-{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}发送结果O->PLC:{c.Value[9]},{c.Value[10]}");
                                    complete = false;
                                    Aoi2Message.TryRemove(c.Key, out _);
                                }
                            }
                            break;

                        case 3:
                            var destination3 = value.Bytes1.Skip(34).Take(10).ToArray();
                            var destination4 = value.Bytes2.Skip(34).Take(10).ToArray();
                            beforeDt = DateTime.Now;
                            KeyValuePair<byte[], byte[]> bb = default;
                            KeyValuePair<byte[], byte[]> bb1 = default;
                            KeyValuePair<byte[], byte[]> cc = default;
                            KeyValuePair<byte[], byte[]> cc1 = default;
                            var isSenndTakePhoto = false;
                            var isSenndResult = true;
                            while ((inPhoto || photoCompleted || complete) && (timeOut = GetElapsedMs(beforeDt)) < _timeout)
                            {
                                bb = FindMessage(Aoi1Message, 1, destination3);
                                bb1 = FindMessage(Aoi2Message, 1, destination4);
                                if (IsValidKvp(bb) && IsValidKvp(bb1))
                                {
                                    var re = CreateResponseCopy(value.BytesOriginal, 1, 1, 0);
                                    _remoteUdp?.Send(_plcIpEndPoint, re);
                                    isSenndTakePhoto = true;
                                    Logger?.Info($"{label19.Text}-{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}拍照完成O->PLC:");
                                    photoCompleted = false;
                                    Aoi1Message.TryRemove(bb.Key, out _);
                                    Aoi2Message.TryRemove(bb1.Key, out _);
                                }

                                cc = FindMessage(Aoi1Message, 2, destination3);
                                cc1 = FindMessage(Aoi2Message, 2, destination4);
                                if (IsValidKvp(cc) && IsValidKvp(cc1))
                                {
                                    var re = CreateResponseCopy(value.BytesOriginal, 1, 2, 0);
                                    switch (value.ty)
                                    {
                                        case 1:
                                            re[9] = cc.Value[9];
                                            re[10] = cc.Value[10];
                                            re[89] = cc.Value[11];
                                            re[90] = cc.Value[90];
                                            break;
                                        case 2:
                                            re[11] = cc1.Value[9];
                                            re[12] = cc1.Value[10];
                                            re[89] = cc1.Value[11];
                                            re[90] = cc1.Value[90];
                                            break;
                                        case 3:
                                            re[9] = cc.Value[9];
                                            re[10] = cc.Value[10];
                                            re[11] = cc1.Value[9];
                                            re[12] = cc1.Value[10];
                                            if (cc.Value[11] == 2 || cc1.Value[11] == 2)
                                            {
                                                re[89] = 2;
                                            }
                                            if (cc.Value[90] > 0)
                                            {
                                                re[90] = cc.Value[90];
                                            }
                                            if (cc1.Value[90] > 0)
                                            {
                                                re[90] = cc1.Value[90];
                                            }
                                            break;
                                    }

                                    _remoteUdp?.Send(_plcIpEndPoint, re);
                                    isSenndResult = false;
                                    Logger?.Info($"{label19.Text}-{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}发送结果O->PLC:{cc.Value[9]},{cc.Value[10]},{cc1.Value[9]},{cc1.Value[10]}");
                                    complete = false;
                                    Aoi1Message.TryRemove(cc.Key, out _);
                                    Aoi2Message.TryRemove(cc1.Key, out _);
                                }
                            }
                            if (isSenndTakePhoto && isSenndResult)
                            {
                                var re = CreateResponseCopy(value.BytesOriginal, 1, 2, 0);
                                if (IsValidKvp(cc) && !IsValidKvp(cc1))
                                {
                                    switch (value.ty)
                                    {
                                        case 1: re[9] = cc.Value[9]; re[10] = cc.Value[10]; break;
                                        case 2: re[11] = WaiGuan; re[12] = Color; break;
                                        case 3: re[9] = cc.Value[9]; re[10] = cc.Value[10]; re[11] = WaiGuan; re[12] = Color; break;
                                    }
                                    _remoteUdp?.Send(_plcIpEndPoint, re);
                                    Logger?.Info($"{label19.Text}-{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}发送结果O->PLC:{cc.Value[9]},{cc.Value[10]},{WaiGuan},{Color}");
                                    complete = false;
                                    Aoi1Message.TryRemove(cc.Key, out _);
                                }
                                else if (!IsValidKvp(cc) && IsValidKvp(cc1))
                                {
                                    switch (value.ty)
                                    {
                                        case 1: re[9] = WaiGuan; re[10] = Color; break;
                                        case 2: re[11] = cc1.Value[9]; re[12] = cc1.Value[10]; break;
                                        case 3: re[9] = WaiGuan; re[10] = Color; re[11] = cc1.Value[9]; re[12] = cc1.Value[10]; break;
                                    }
                                    _remoteUdp?.Send(_plcIpEndPoint, re);
                                    Logger?.Info($"{label19.Text}-{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}发送结果O->PLC:{WaiGuan},{Color},{cc1.Value[9]},{cc1.Value[10]}");
                                    complete = false;
                                    Aoi2Message.TryRemove(cc1.Key, out _);
                                }
                                else
                                {
                                    switch (value.ty)
                                    {
                                        case 1: re[9] = WaiGuan; re[10] = Color; break;
                                        case 2: re[11] = WaiGuan; re[12] = Color; break;
                                        case 3: re[9] = WaiGuan; re[10] = Color; re[11] = WaiGuan; re[12] = Color; break;
                                    }
                                    _remoteUdp?.Send(_plcIpEndPoint, re);
                                    Logger?.Info($"{label19.Text}-{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}发送结果O->PLC:{WaiGuan},{Color},{WaiGuan},{Color}");
                                    complete = false;
                                }
                            }
                            break;

                        default:
                            break;
                    }
                }
                catch (Exception e)
                {
                    Logger?.Error("返回消息给PLC出错:" + e.Message, e);
                }
            }
        }

        #endregion 成品

        #region 出版

        /// <summary>
        /// 转发AOI发送的消息给PLC
        /// </summary>
        private void CbTransmit()
        {
            while (true)
            {
                _tokenSource?.Token.ThrowIfCancellationRequested();
                try
                {
                    long timeOut;
                    DateTime beforeDt = default;
                    var inPhoto = false;
                    var photoCompleted = true;
                    var complete = true;
                    PendingData value = default;
                    if (RemoteQueue == null || _plcIpEndPoint == null || RemoveQueue == null || _localUdp == null || _localUdp1 == null)
                    {
                        continue;
                    }
                    RemoteQueue.Dequeue(out value);
                    Logger?.Info(value);
                    switch (value.Type)
                    {
                        case 1:
                            var destination1 = value.Bytes1.Skip(34).Take(10).ToArray();
                            beforeDt = DateTime.Now;
                            while ((inPhoto || photoCompleted || complete) && (timeOut = GetElapsedMs(beforeDt)) < _timeout)
                            {
                                var b = FindMessage(Aoi1Message, 1, destination1);
                                if (IsValidKvp(b))
                                {
                                    var re = CreateResponseCopy(value.BytesOriginal, 1, 1, 0);
                                    _remoteUdp?.SendAsync(_plcIpEndPoint, re);
                                    Logger?.Info($"{label19.Text}-{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}拍照完成O->PLC:");
                                    Logger?.Info(re);
                                    Logger?.Info("----------------------------------------------------");
                                    photoCompleted = false;
                                    Aoi1Message.TryRemove(b.Key, out _);
                                }

                                var c = FindMessage(Aoi1Message, 2, destination1);
                                if (IsValidKvp(c))
                                {
                                    var re = CreateResponseCopy(value.BytesOriginal, 1, 2, 0);
                                    re[13] = (byte)(c.Value[11] == 2 ? 2 : 1);
                                    re[9] = c.Value[12];
                                    var re2 = re.Take(90);
                                    var waferData = c.Value.Skip(90).Take(value.BytesOriginal.Length - 90);
                                    _remoteUdp?.SendAsync(_plcIpEndPoint, re2.Concat(waferData).ToArray());
                                    Logger?.Info($"{label19.Text}-{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}发送结果O->PLC:");
                                    Logger?.Info(re);
                                    Logger?.Info("----------------------------------------------------");
                                    complete = false;
                                    Aoi1Message.TryRemove(c.Key, out _);
                                }
                            }
                            break;

                        case 2:
                            var destination2 = value.Bytes2.Skip(34).Take(10).ToArray();
                            beforeDt = DateTime.Now;
                            while ((inPhoto || photoCompleted || complete) && (timeOut = GetElapsedMs(beforeDt)) < _timeout)
                            {
                                var b = FindMessage(Aoi2Message, 1, destination2);
                                if (IsValidKvp(b))
                                {
                                    var re = CreateResponseCopy(value.BytesOriginal, 1, 1, 0);
                                    _remoteUdp?.SendAsync(_plcIpEndPoint, re);
                                    Logger?.Info($"{label19.Text}-{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}拍照完成O->PLC:");
                                    Logger?.Info(re);
                                    Logger?.Info("----------------------------------------------------");
                                    photoCompleted = false;
                                    Aoi2Message.TryRemove(b.Key, out _);
                                }

                                var c = FindMessage(Aoi2Message, 2, destination2);
                                if (IsValidKvp(c))
                                {
                                    var re = CreateResponseCopy(value.BytesOriginal, 1, 2, 0);
                                    re[13] = (byte)(c.Value[11] == 2 ? 2 : 1);
                                    re[11] = c.Value[12];
                                    var re2 = re.Take(90).ToArray();
                                    var waferData = c.Value.Skip(90).Take(value.BytesOriginal.Length - 90).ToArray();
                                    for (int i = 0; i < waferData.Length - 1; i++)
                                    {
                                        if (waferData[i] == 1)
                                        {
                                            waferData[i] = 2;
                                        }
                                    }
                                    _remoteUdp?.SendAsync(_plcIpEndPoint, re2.Concat(waferData).ToArray());
                                    Logger?.Info($"{label19.Text}-{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}发送结果O->PLC:");
                                    Logger?.Info(re);
                                    Logger?.Info("----------------------------------------------------");
                                    complete = false;
                                    Aoi2Message.TryRemove(c.Key, out _);
                                }
                            }
                            break;

                        case 3:
                            var destination3 = value.Bytes1.Skip(34).Take(10).ToArray();
                            var destination4 = value.Bytes2.Skip(34).Take(10).ToArray();
                            beforeDt = DateTime.Now;
                            while ((inPhoto || photoCompleted || complete) && (timeOut = GetElapsedMs(beforeDt)) < _timeout)
                            {
                                var b = FindMessage(Aoi1Message, 1, destination3);
                                var b1 = FindMessage(Aoi2Message, 1, destination4);
                                if (IsValidKvp(b) && IsValidKvp(b1))
                                {
                                    var re = CreateResponseCopy(value.BytesOriginal, 1, 1, 0);
                                    _remoteUdp?.SendAsync(_plcIpEndPoint, re);
                                    Logger?.Info($"{label19.Text}-{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}拍照完成O->PLC:");
                                    Logger?.Info(re);
                                    Logger?.Info("----------------------------------------------------");
                                    photoCompleted = false;
                                    Aoi1Message.TryRemove(b.Key, out _);
                                    Aoi2Message.TryRemove(b1.Key, out _);
                                }

                                var c = FindMessage(Aoi1Message, 2, destination3);
                                var c1 = FindMessage(Aoi2Message, 2, destination4);
                                if (IsValidKvp(c) && IsValidKvp(c1))
                                {
                                    var re = CreateResponseCopy(value.BytesOriginal, 1, 2, 0);
                                    re[13] = (byte)((c.Value[11] == 2 || c1.Value[11] == 2) ? 2 : 1);
                                    re[11] = c1.Value[12];
                                    re[9] = c.Value[12];
                                    var re2 = re.Take(90).ToArray();
                                    var waferData = c.Value.Skip(90).Take(value.BytesOriginal.Length - 90).ToArray();
                                    var waferData1 = c1.Value.Skip(90).Take(value.BytesOriginal.Length - 90).ToArray();
                                    for (int i = 0; i < waferData.Length - 1; i++)
                                    {
                                        if (waferData[i] == waferData1[i])
                                        {
                                            if (waferData[i] == 1)
                                            {
                                                waferData[i] = 3;
                                            }
                                        }
                                        else
                                        {
                                            if (waferData[i] - waferData1[i] == 1)
                                            {
                                                waferData[i] = 1;
                                            }
                                            else if (waferData[i] - waferData1[i] == -1)
                                            {
                                                waferData[i] = 2;
                                            }
                                        }
                                    }

                                    _remoteUdp?.SendAsync(_plcIpEndPoint, re2.Concat(waferData).ToArray());
                                    Logger?.Info($"{label19.Text}-{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}发送结果O->PLC:");
                                    Logger?.Info(re);
                                    Logger?.Info("----------------------------------------------------");
                                    complete = false;
                                    Aoi1Message.TryRemove(c.Key, out _);
                                    Aoi2Message.TryRemove(c1.Key, out _);
                                }
                            }
                            break;

                        default:
                            break;
                    }
                }
                catch (Exception e)
                {
                    Logger?.Error("返回消息给PLC出错:" + e.Message, e);
                }
            }
        }

        #endregion 出版

        private void button3_Click(object sender, EventArgs e)
        {
            try
            {
                button3.Enabled = false;
                RemoteBnt.Enabled = true;
                if (_remoteUdp == null) return;
                _remoteUdp.Stop();
                _remoteUdp.Dispose();
                _remoteUdp = null;
            }
            catch (Exception exception)
            {
                Console.WriteLine(exception);
                throw;
            }
        }

        private void button4_Click(object sender, EventArgs e)
        {
            try
            {
                button4.Enabled = false;
                button1.Enabled = true;
                if (_localUdp == null) return;
                _localUdp.Stop();
                _localUdp.Dispose();
                _localUdp = null;
            }
            catch (Exception exception)
            {
                Console.WriteLine(exception);
                throw;
            }
        }

        private void button5_Click(object sender, EventArgs e)
        {
            try
            {
                if (CB.Checked)
                {
                    CP.Enabled = true;
                }
                else
                {
                    CB.Enabled = true;
                }
                button5.Enabled = false;
                button2.Enabled = true;
                if (_localUdp1 == null) return;
                _localUdp1.Stop();
                _localUdp1.Dispose();
                _localUdp1 = null;
            }
            catch (Exception exception)
            {
                Console.WriteLine(exception);
                throw;
            }
        }

        /// <summary>
        /// 自旋锁
        /// </summary>
        public void LockMethod(Action action)
        {
            bool gotLock = false;
            try
            {
                gotLock = false;
                spinLock.Enter(ref gotLock);
                action();
            }
            catch (Exception)
            {
            }
            finally { if (gotLock) spinLock.Exit(); }
        }

        /// <summary>
        /// 自旋锁
        /// </summary>
        public void LockMethod1(Action action)
        {
            bool gotLock = false;
            try
            {
                gotLock = false;
                spinLock1.Enter(ref gotLock);
                action();
            }
            catch (Exception)
            {
            }
            finally { if (gotLock) spinLock1.Exit(); }
        }

        /// <summary>
        /// 锁
        /// </summary>
        public void LockMethod(Action action, object stauts)
        {
            try
            {
                lock (stauts)
                {
                    action();
                }
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// 锁
        /// </summary>
        public void LockMethod1(Action action, object stauts)
        {
            try
            {
                lock (stauts)
                {
                    action();
                }
            }
            catch (Exception)
            {
            }
        }

        private static string path = System.AppDomain.CurrentDomain.SetupInformation.ApplicationBase + "setingTcp.Json";

        private async void SaveJsonData()
        {
            var json = new JsonObject();
            json.Add("CB", CB.Checked);
            json.Add("CP", CP.Checked);
            json.Add("numericUpDown1", numericUpDown1.Value);
            json.Add("numericUpDown2", numericUpDown2.Value);
            json.Add("PlcIp", PlcIp.Text);
            json.Add("PlcPort", PlcPort.Text);
            json.Add("Plc_oneIp", Plc_oneIp.Text);
            json.Add("Plc_onePort", Plc_onePort.Text);

            json.Add("Aoi1_oneIp", Aoi1_oneIp.Text);
            json.Add("Aoi_onePort", Aoi_onePort.Text);
            json.Add("Aoi1Ip", Aoi1Ip.Text);
            json.Add("Aoi1Port", Aoi1Port.Text);

            json.Add("Aoi2_oneIp", Aoi2_oneIp.Text);
            json.Add("Aoi2_onePort", Aoi2_onePort.Text);
            json.Add("Aoi2Ip", Aoi2Ip.Text);
            json.Add("Aoi2Port", Aoi2Port.Text);
            json.Add("PassWord", passWord);
            json.Add("IsUse", IsUse);
            json.Add("WaiGuan", WaiGuan);
            json.Add("Color", Color);
            json.Add("StartIndex", StartIndex);
            json.Add("Length", Length);
            json.Add("StartIndex1", StartIndex1);
            json.Add("Length1", Length1);
            json.Add("OneTakePhoto", OneTakePhoto);
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            // Create a file to write to.
            using (FileStream FS = File.Create(path))
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                await JsonSerializer.SerializeAsync(FS, json, options);
                await FS.FlushAsync();
            }
        }

        private bool InitParam()
        {
            try
            {
                if (File.Exists(path))
                {
                    var jsonString = File.ReadAllText(path);
                    var jsonNode = JsonNode.Parse(jsonString)!;
                    if (jsonNode!["CB"]!.GetValue<bool>())
                    {
                        CB.Checked = true;
                    }
                    if (jsonNode!["CP"]!.GetValue<bool>())
                    {
                        CP.Checked = true;
                    }
                    numericUpDown1.Value = jsonNode!["numericUpDown1"]!.GetValue<decimal>();
                    numericUpDown2.Value = jsonNode!["numericUpDown2"]!.GetValue<decimal>();
                    PlcIp.Text = jsonNode!["PlcIp"]!.GetValue<string>();
                    PlcPort.Text = jsonNode!["PlcPort"]!.GetValue<string>();
                    Plc_oneIp.Text = jsonNode!["Plc_oneIp"]!.GetValue<string>();
                    Plc_onePort.Text = jsonNode!["Plc_onePort"]!.GetValue<string>();

                    Aoi1_oneIp.Text = jsonNode!["Aoi1_oneIp"]!.GetValue<string>();
                    Aoi_onePort.Text = jsonNode!["Aoi_onePort"]!.GetValue<string>();
                    Aoi1Ip.Text = jsonNode!["Aoi1Ip"]!.GetValue<string>();
                    Aoi1Port.Text = jsonNode!["Aoi1Port"]!.GetValue<string>();

                    Aoi2_oneIp.Text = jsonNode!["Aoi2_oneIp"]!.GetValue<string>();
                    Aoi2_onePort.Text = jsonNode!["Aoi2_onePort"]!.GetValue<string>();
                    Aoi2Ip.Text = jsonNode!["Aoi2Ip"]!.GetValue<string>();
                    Aoi2Port.Text = jsonNode!["Aoi2Port"]!.GetValue<string>();
                    passWord = jsonNode!["PassWord"]!.GetValue<string>();
                    IsUse = jsonNode!["IsUse"]!.GetValue<bool>();
                    WaiGuan = jsonNode!["WaiGuan"]!.GetValue<byte>();
                    Color = jsonNode!["Color"]!.GetValue<byte>();
                    StartIndex = jsonNode!["StartIndex"]!.GetValue<int>();
                    Length = jsonNode!["Length"]!.GetValue<int>();
                    StartIndex1 = jsonNode!["StartIndex1"]!.GetValue<int>();
                    Length1 = jsonNode!["Length1"]!.GetValue<int>();
                    OneTakePhoto = jsonNode!["OneTakePhoto"]!.GetValue<bool>();
                    return true;
                }
                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private void CB_CheckedChanged(object sender, EventArgs e)
        {
            if (CB.Checked)
            {
                CB.Enabled = false;
                if (_tokenSource1 != null)
                {
                    _tokenSource1.Cancel();
                    _tokenSource1.Dispose();
                    _tokenSource1 = null;
                }
                if (_tokenSource == null)
                {
                    _tokenSource = new();
                }
                Task.Factory.StartNew(CbTransmit, _tokenSource.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                CP.Enabled = true;
            }
        }

        private void CP_CheckedChanged(object sender, EventArgs e)
        {
            if (!CP.Checked) return;
            CP.Enabled = false;
            if (_tokenSource != null)
            {
                _tokenSource.Cancel();
                _tokenSource.Dispose();
                _tokenSource = null;
            }
            if (_tokenSource1 == null)
            {
                _tokenSource1 = new();
            }
            Task.Factory.StartNew(Transmit, _tokenSource1.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            CB.Enabled = true;
        }

        private void SignalForward_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (_tokenSource != null)
            {
                _tokenSource.Cancel();
                _tokenSource.Dispose();
            }
            if (_tokenSource1 != null)
            {
                _tokenSource1.Cancel();
                _tokenSource1.Dispose();
            }

            if (_tokenSource2 == null) return;
            _tokenSource2.Cancel();
            _tokenSource2.Dispose();
        }

        private void numericUpDown1_ValueChanged(object sender, EventArgs e)
        {
            _timeout = (int)numericUpDown1.Value;
        }

        private void button6_Click(object sender, EventArgs e)
        {
            var login = new LoginScreen(passWord);
            if (login.ShowDialog() == DialogResult.OK)
            {
                SetAllControlsEnabled(true);
            }
            else
            {
                SetAllControlsEnabled(false);
            }
        }

        private void numericUpDown2_ValueChanged(object sender, EventArgs e)
        {
            _timeout1 = (int)numericUpDown2.Value;
        }

        private void SignalForwardUdp_Load(object sender, EventArgs e)
        {
            string file = "D:\\logs";
            if (!File.Exists(file))
            {
                var paths = Directory.GetDirectories(file);
                foreach (var item in paths)
                {
                    DirectoryInfo directoryInfo = new(item);
                    DateTime creationTime = directoryInfo.CreationTime;
                    if (DateTime.Now.AddDays(-7) > creationTime)
                    {
                        Directory.Delete(item, true);
                    }
                }
            }
        }
    }

    /// <summary>
    /// 待处理数据
    /// </summary>
    public class PendingData
    {
        public int ty { get; set; }

        public int Type { get; set; }

        public byte[] Bytes1 { get; set; }

        public byte[] Bytes2 { get; set; }

        public byte[] BytesOriginal { get; set; }
    }
}

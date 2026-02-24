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
            //PlcIp.DataBindings.Add("Enabled", RemoteBnt, "Enabled");
            //PlcPort.DataBindings.Add("Enabled", RemoteBnt, "Enabled");
            //Plc_oneIp.DataBindings.Add("Enabled", RemoteBnt, "Enabled");
            //Plc_onePort.DataBindings.Add("Enabled", RemoteBnt, "Enabled");

            //Aoi1_oneIp.DataBindings.Add("Enabled", button1, "Enabled");
            //Aoi_onePort.DataBindings.Add("Enabled", button1, "Enabled");
            //Aoi1Ip.DataBindings.Add("Enabled", button1, "Enabled");
            //Aoi1Port.DataBindings.Add("Enabled", button1, "Enabled");

            //Aoi2_oneIp.DataBindings.Add("Enabled", button2, "Enabled");
            //Aoi2_onePort.DataBindings.Add("Enabled", button2, "Enabled");
            //Aoi2Ip.DataBindings.Add("Enabled", button2, "Enabled");
            //Aoi2Port.DataBindings.Add("Enabled", button2, "Enabled");
            //numericUpDown1.DataBindings.Add("Enabled", button2, "Enabled");

            PlcIp.Enabled = false;
            PlcPort.Enabled = false;
            Plc_oneIp.Enabled = false;
            Plc_onePort.Enabled = false;

            Aoi1_oneIp.Enabled = false;
            Aoi_onePort.Enabled = false;
            Aoi1Ip.Enabled = false;
            Aoi1Port.Enabled = false;

            Aoi2_oneIp.Enabled = false;
            Aoi2_onePort.Enabled = false;
            Aoi2Ip.Enabled = false;
            Aoi2Port.Enabled = false;
            numericUpDown1.Enabled = false;
            numericUpDown2.Enabled = false;

            InitParam();
            CallOnClick(RemoteBnt);
            CallOnClick(button1);
            CallOnClick(button2);

            //Task.Factory.StartNew(Remove, _tokenSource2.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        /// <summary>
        /// 触发按钮的点击事件
        /// </summary>
        /// <param name="btn"></param>
        private void CallOnClick(Button btn)
        {
            //建立一个类型
            Type t = typeof(Button);
            //参数对象
            object[] p = new object[1];
            //产生方法
            MethodInfo m = t.GetMethod("OnClick", BindingFlags.NonPublic | BindingFlags.Instance);
            //参数赋值。传入函数
            p[0] = EventArgs.Empty;
            //调用
            m.Invoke(btn, p);
            return;
        }

        #region 同步方式

        //public void Communication()
        //{
        //    while (true)
        //    {
        //        byte[] data;
        //        if (_localUdp == null || _localUdp1 == null || RemoteQueue == null)
        //        {
        //            continue;
        //        }
        //        RemoteQueue.Dequeue(out data);
        //        switch (data[43])
        //        {
        //            case 1:
        //                if (data[3] == 1)
        //                {
        //                    _localUdp.Send(_aoi1PortEndPoint, data);
        //                    bool control = true;
        //                    while (control)
        //                    {
        //                        IPEndPoint rEndPoint = default;
        //                        byte[] buff = _localUdp.Server.Receive(ref rEndPoint);
        //                        if (buff != default)
        //                        {
        //                            _remoteUdp.SendAsync(_plcIpEndPoint, buff);
        //                        }
        //                        if (buff[2] == 2)
        //                        {
        //                            break;
        //                        }
        //                    }
        //                }
        //                break;

        //            case 2:
        //                if (data[3] == 1)
        //                {
        //                    _localUdp1.Send(_aoi2PortEndPoint, data);
        //                    bool control = true;
        //                    while (control)
        //                    {
        //                        IPEndPoint rEndPoint = default;
        //                        byte[] buff = _localUdp1.Server.Receive(ref rEndPoint);
        //                        if (buff != default)
        //                        {
        //                            _remoteUdp.SendAsync(_plcIpEndPoint, buff);
        //                        }
        //                        if (buff[2] == 2)
        //                        {
        //                            break;
        //                        }
        //                    }
        //                }
        //                break;

        //            case 3:
        //                if (data[3] == 1)
        //                {
        //                    //事务列表
        //                    List<Task<byte[]>> tasks = new List<Task<byte[]>>();
        //                    _localUdp.SendAsync(_aoi1PortEndPoint, data);
        //                    _localUdp1.SendAsync(_aoi2PortEndPoint, data);
        //                    bool control = true;
        //                    while (control)
        //                    {
        //                        Task<byte[]> result = new Task<byte[]>((() =>
        //                        {
        //                            IPEndPoint ipEnd = default;
        //                            return _localUdp.Server.Receive(ref ipEnd);
        //                        }));
        //                        tasks.Add(result);
        //                        Task<byte[]> result1 = new Task<byte[]>((() =>
        //                        {
        //                            IPEndPoint ipEnd = default;
        //                            return _localUdp1.Server.Receive(ref ipEnd);
        //                        }));
        //                        tasks.Add(result1);
        //                        result.Start();
        //                        result1.Start();

        //                    List<byte[]> r = new List<byte[]>();
        //                    foreach (var item in tasks)
        //                    {
        //                        r.Add(item.Result);
        //                    }

        //                    if (r[0][2] == 2 && r[1][2] == 2)
        //                    {
        //                        break;
        //                    }
        //                }
        //            }
        //            break;

        //        default:
        //            break;
        //    }
        //}
        //}

        #endregion 同步方式

        public byte[] GetBytes()
        {
            // 使用当前时间的毫秒部分作为随机种子
            int seed = DateTime.Now.Millisecond;
            Random random = new Random(seed);
            // 创建一个5位的字节数组
            byte[] byteArray = new byte[10];
            // 填充字节数组
            random.NextBytes(byteArray);
            return byteArray;
        }

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
                        //Logger?.Info("----------------------------------------------------");
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
                            int xinhao = 0;
                            //if (CB.Checked)
                            //{
                            //    xinhao = dataBytes[65];
                            //}
                            //else if (CP.Checked)
                            //{
                            xinhao = dataBytes[66];
                            //}
                            var shifoupaizhao = dataBytes[3];
                            switch (xinhao)
                            {
                                case 1:
                                    if (shifoupaizhao == 1)
                                    {
                                        var newBytes = new byte[dataBytes.Length];
                                        newBytes[3] = 1;
                                        var data = dataBytes.Skip(34).Take(44 - 34).ToArray();
                                        for (var i = 0; i < data.Length; i++)
                                        {
                                            newBytes[34 + i] = data[i];
                                        }
                                        var data2 = dataBytes.Skip(StartIndex).Take(Length).ToArray();
                                        for (var i = 0; i < data2.Length; i++)
                                        {
                                            newBytes[44 + i] = data2[i];
                                        }
                                        var newBytes1 = new byte[128];
                                        newBytes1[3] = 1;
                                        if (IsUse)
                                        {
                                            newBytes1[20] = 1;
                                        }
                                        var data1 = dataBytes.Skip(34).Take(44 - 34).ToArray();
                                        var id = GetBytes();
                                        for (var i = 0; i < data1.Length; i++)
                                        {
                                            newBytes1[34 + i] = id[i];
                                        }
                                        if (OneTakePhoto)
                                        {
                                            if (_aoi1PortEndPoint != null) _localUdp?.Send(_aoi1PortEndPoint, newBytes);
                                            Logger?.Info("O->AOI1");
                                            var datas = new PendingData();
                                            datas.Type = 1;
                                            datas.ty = 1;
                                            datas.Bytes1 = newBytes;
                                            datas.Bytes2 = newBytes1;
                                            datas.BytesOriginal = dataBytes;
                                            RemoteQueue?.Enqueue(datas);
                                        }
                                        else
                                        {
                                            if (_aoi2PortEndPoint != null) _localUdp1?.Send(_aoi2PortEndPoint, newBytes1);
                                            Logger?.Info("O->AOI2");
                                            if (_aoi1PortEndPoint != null) _localUdp?.Send(_aoi1PortEndPoint, newBytes);
                                            Logger?.Info("O->AOI1");
                                            var datas = new PendingData();
                                            datas.Type = 3;
                                            datas.ty = 1;
                                            datas.Bytes1 = newBytes;
                                            datas.Bytes2 = newBytes1;
                                            datas.BytesOriginal = dataBytes;
                                            RemoteQueue?.Enqueue(datas);
                                        }
                                    }
                                    break;

                                case 2:
                                    if (shifoupaizhao == 1)
                                    {
                                        var newBytes = new byte[dataBytes.Length];
                                        newBytes[3] = 1;
                                        var data = dataBytes.Skip(44).Take(54 - 44).ToArray();
                                        for (var i = 0; i < data.Length; i++)
                                        {
                                            newBytes[34 + i] = data[i];
                                        }
                                        var data2 = dataBytes.Skip(StartIndex1).Take(Length1).ToArray();
                                        for (var i = 0; i < data2.Length; i++)
                                        {
                                            newBytes[44 + i] = data2[i];
                                        }

                                        var newBytes1 = new byte[128];
                                        newBytes1[3] = 1;
                                        if (IsUse)
                                        {
                                            newBytes1[20] = 1;
                                        }
                                        var data1 = dataBytes.Skip(34).Take(44 - 34).ToArray();
                                        var id = GetBytes();
                                        for (var i = 0; i < data1.Length; i++)
                                        {
                                            newBytes1[34 + i] = id[i];
                                        }
                                        if (OneTakePhoto)
                                        {
                                            if (_aoi2PortEndPoint != null) _localUdp1?.Send(_aoi2PortEndPoint, newBytes);
                                            Logger?.Info("O->AOI2");
                                            var datas = new PendingData();
                                            datas.Type = 2;
                                            datas.ty = 2;
                                            datas.Bytes1 = newBytes1;
                                            datas.Bytes2 = newBytes;
                                            datas.BytesOriginal = dataBytes;
                                            RemoteQueue?.Enqueue(datas);
                                        }
                                        else
                                        {
                                            if (_aoi1PortEndPoint != null) _localUdp?.Send(_aoi1PortEndPoint, newBytes1);
                                            Logger?.Info("O->AOI1");
                                            if (_aoi2PortEndPoint != null) _localUdp1?.Send(_aoi2PortEndPoint, newBytes);
                                            Logger?.Info("O->AOI2");
                                            var datas = new PendingData();
                                            datas.Type = 3;
                                            datas.ty = 2;
                                            datas.Bytes1 = newBytes1;
                                            datas.Bytes2 = newBytes;
                                            datas.BytesOriginal = dataBytes;
                                            RemoteQueue?.Enqueue(datas);
                                        }
                                    }
                                    break;

                                case 3:
                                    if (shifoupaizhao == 1)
                                    {
                                        var newBytes = new byte[dataBytes.Length];
                                        newBytes[3] = 1;
                                        newBytes[5] = 1;
                                        var data = dataBytes.Skip(34).Take(44 - 34).ToArray();
                                        for (var i = 0; i < data.Length; i++)
                                        {
                                            newBytes[34 + i] = data[i];
                                        }
                                        var data2 = dataBytes.Skip(StartIndex).Take(Length).ToArray();
                                        for (var i = 0; i < data2.Length; i++)
                                        {
                                            newBytes[44 + i] = data2[i];
                                        }

                                        var newBytes1 = new byte[128];
                                        newBytes1[3] = 1;
                                        var data1 = dataBytes.Skip(44).Take(54 - 44).ToArray();
                                        for (var i = 0; i < data1.Length; i++)
                                        {
                                            newBytes1[34 + i] = data1[i];
                                        }
                                        var data3 = dataBytes.Skip(StartIndex1).Take(Length1).ToArray();
                                        for (var i = 0; i < data3.Length; i++)
                                        {
                                            newBytes1[44 + i] = data3[i];
                                        }

                                        if (_aoi1PortEndPoint != null) _localUdp?.Send(_aoi1PortEndPoint, newBytes);
                                        Logger?.Info("O->AOI1");
                                        if (_aoi2PortEndPoint != null) _localUdp1?.Send(_aoi2PortEndPoint, newBytes1);
                                        Logger?.Info("O->AOI2");
                                        var datas = new PendingData();
                                        datas.Type = 3;
                                        datas.ty = 3;
                                        datas.Bytes1 = newBytes;
                                        datas.Bytes2 = newBytes1;
                                        datas.BytesOriginal = dataBytes;
                                        RemoteQueue?.Enqueue(datas);
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
                _localUdp.DataReceived += (o, bytes) =>
                {
                    Logger?.Info($"{label19.Text}-{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}接收AOI1消息:");
                    //Logger?.Info(bytes);
                    //Logger?.Info("----------------------------------------------------");

                    var wuyiyi = bytes[0];
                    var jiuxu = bytes[1];
                    var caozhuo = bytes[2];
                    var liushuihao = bytes.Skip(34).Take(10);

                    //switch (bytes)
                    //{
                    //    case byte[] n when n[1] == 1 && n[2] == 3:
                    //        if (_remoteUdp != null || _plcIpEndPoint != null)
                    //            _remoteUdp?.SendAsync(_plcIpEndPoint, bytes);
                    //        break;

                    //    case byte[] n when n[1] == 0 && n[2] == 3:
                    //        if (_remoteUdp != null || _plcIpEndPoint != null)
                    //            _remoteUdp?.SendAsync(_plcIpEndPoint, bytes);
                    //        break;

                    //    case byte[] n when (n[2] == 1 || n[2] == 2 || n[2] == 0) && (n.Skip(34).Take(10).SequenceEqual(_moRen1) || n.Skip(34).Take(10).SequenceEqual(_moRen)):
                    //        break;

                    //    case byte[] n when (n[0] == 0 && n[1] == 1 && n[2] == 0):
                    //        break;

                    //    default:
                    //        Aoi1Message.TryAdd(bytes, bytes);
                    //        Logger?.Info($"{label19.Text}-{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}添加到AOI1消息列表:");
                    //        //Logger?.Info(bytes);
                    //        //Logger?.Info("----------------------------------------------------");
                    //        break;
                    //}

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
                        //Logger?.Info("AOI流水号为空");
                        Logger?.Info(bytes);
                        Logger?.Info("...");
                    }
                    else if (wuyiyi == 0 && jiuxu == 1 && caozhuo == 0)
                    {
                        Logger?.Info("....");
                        Logger?.Info(bytes);
                    }
                    else
                    {
                        Logger?.Info(".....");
                        Logger?.Info(bytes);
                        Aoi1Message.TryAdd(bytes, bytes);
                        Logger?.Info($"{label19.Text}-{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}添加到AOI1消息列表:");
                    }

                    //if (bytes[1] == 1 && bytes[2] == 3) //&& bytes.Skip(34).Take(10).SequenceEqual(_moRen1)
                    //{
                    //    if (_remoteUdp != null || _plcIpEndPoint != null)
                    //    {
                    //        _remoteUdp?.SendAsync(_plcIpEndPoint, bytes);
                    //    }
                    //}
                    //else if (bytes[1] == 0 && bytes[2] == 3) // && bytes.Skip(34).Take(10).SequenceEqual(_moRen1)
                    //{
                    //    if (_remoteUdp != null || _plcIpEndPoint != null)
                    //    {
                    //        _remoteUdp?.SendAsync(_plcIpEndPoint, bytes);
                    //    }
                    //}
                    //else if ((bytes[2] == 1 || bytes[2] == 2 || bytes[2] == 0) && (bytes.Skip(34).Take(10).SequenceEqual(_moRen1) || bytes.Skip(34).Take(10).SequenceEqual(_moRen)))
                    //{
                    //}
                    //else if (bytes[2] != 0)
                    //{
                    //    LockMethod(() => { Aoi1Message.Add(bytes); });
                    //    Logger?.Info($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}添加到AOI1消息列表:");
                    //    Logger?.Info(bytes);
                    //    Logger?.Info("----------------------------------------------------");
                    //}
                };
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
                PlcIp.Enabled = false;
                PlcPort.Enabled = false;
                Plc_oneIp.Enabled = false;
                Plc_onePort.Enabled = false;

                Aoi1_oneIp.Enabled = false;
                Aoi_onePort.Enabled = false;
                Aoi1Ip.Enabled = false;
                Aoi1Port.Enabled = false;

                Aoi2_oneIp.Enabled = false;
                Aoi2_onePort.Enabled = false;
                Aoi2Ip.Enabled = false;
                Aoi2Port.Enabled = false;
                numericUpDown1.Enabled = false;
                numericUpDown2.Enabled = false;
                if (Logger == null) return;
                button2.Enabled = false;
                button5.Enabled = true;
                _aoi2PortEndPoint =
                    new IPEndPoint(IPAddress.Parse(Aoi2Ip.Text.Trim()), int.Parse(Aoi2Port.Text.Trim()));
                _localUdp1 = new UdpSyncServer(IPAddress.Parse(Aoi2_oneIp.Text.Trim()),
                    int.Parse(Aoi2_onePort.Text.Trim()), Logger);
                _localUdp1.DataReceived += (o, bytes) =>
                {
                    Logger?.Info($"{label19.Text}-{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}接收AOI2消息:");
                    //Logger?.Info(bytes);
                    //Logger?.Info("----------------------------------------------------");

                    var wuyiyi = bytes[0];
                    var jiuxu = bytes[1];
                    var caozhuo = bytes[2];
                    var liushuihao = bytes.Skip(34).Take(10);

                    //switch (bytes)
                    //{
                    //    case byte[] n when n[1] == 1 && n[2] == 3:
                    //        if (_remoteUdp != null || _plcIpEndPoint != null)
                    //            _remoteUdp?.SendAsync(_plcIpEndPoint, bytes);
                    //        break;

                    //    case byte[] n when n[1] == 0 && n[2] == 3:
                    //        if (_remoteUdp != null || _plcIpEndPoint != null)
                    //            _remoteUdp?.SendAsync(_plcIpEndPoint, bytes);
                    //        break;

                    //    case byte[] n when (n[2] == 1 || n[2] == 2 || n[2] == 0) && (n.Skip(34).Take(10).SequenceEqual(_moRen1) || n.Skip(34).Take(10).SequenceEqual(_moRen)):
                    //        break;

                    //    case byte[] n when (n[0] == 0 && n[1] == 1 && n[2] == 0):
                    //        break;

                    //    default:
                    //        Aoi2Message.TryAdd(bytes, bytes);
                    //        Logger?.Info($"{label19.Text}-{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}添加到AOI2消息列表:");
                    //        //Logger?.Info(bytes);
                    //        //Logger?.Info("----------------------------------------------------");
                    //        break;
                    //}

                    //if (bytes[1] == 1 && bytes[2] == 3) //&& bytes.Skip(34).Take(10).SequenceEqual(_moRen1)
                    //{
                    //    if (_remoteUdp != null || _plcIpEndPoint != null)
                    //    {
                    //        _remoteUdp?.SendAsync(_plcIpEndPoint, bytes);
                    //    }
                    //}
                    //else if (bytes[1] == 0 && bytes[2] == 3) //&& bytes.Skip(34).Take(10).SequenceEqual(_moRen1)
                    //{
                    //    if (_remoteUdp != null || _plcIpEndPoint != null)
                    //    {
                    //        _remoteUdp?.SendAsync(_plcIpEndPoint, bytes);
                    //    }
                    //}
                    //else if ((bytes[2] == 1 || bytes[2] == 2 || bytes[2] == 0) && (bytes.Skip(34).Take(10).SequenceEqual(_moRen1) || bytes.Skip(34).Take(10).SequenceEqual(_moRen)))
                    //{
                    //}
                    //else if (bytes[2] != 0)
                    //{
                    //    LockMethod1(() => { Aoi2Message.Add(bytes); });
                    //    Logger?.Info($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}添加到AOI2消息列表:");
                    //    Logger?.Info(bytes);
                    //    Logger?.Info("----------------------------------------------------");
                    //}


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
                        Logger?.Info("***");
                    }
                    else if (wuyiyi == 0 && jiuxu == 1 && caozhuo == 0)
                    {
                        Logger?.Info("****");
                        Logger?.Info(bytes);
                    }
                    else
                    {
                        Logger?.Info("*****");
                        Logger?.Info(bytes);
                        Aoi2Message.TryAdd(bytes, bytes);
                        Logger?.Info($"{label19.Text}-{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}添加到AOI2消息列表:");
                    }
                };
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

        #region 旧成品

        /// <summary>
        /// 转发AOI发送的消息给PLC
        /// </summary>
        //private void Transmit()
        //{
        //    while (true)
        //    {
        //        try
        //        {
        //            long timeOut;
        //            DateTime beforeDt = default;
        //            //拍照中
        //            var inPhoto = true;
        //            //拍照完成
        //            var photoCompleted = true;
        //            //检测完成
        //            var complete = true;
        //            //收到的消息
        //            byte[] value = default;
        //            if (RemoteQueue == null || _plcIpEndPoint == null || RemoveQueue == null)
        //            {
        //                continue;
        //            }
        //            RemoteQueue.Dequeue(out value);
        //            Logger?.Info(value);
        //            switch (value[66])
        //            {
        //                case 1:
        //                    var destination1 = value.Skip(34).Take(44 - 34).ToArray();
        //                    timeOut = 0;
        //                    beforeDt = DateTime.Now;
        //                    while ((inPhoto || photoCompleted || complete) && timeOut < 600)
        //                    {
        //                        lock ((Aoi1Message as ICollection).SyncRoot)
        //                        {
        //                            //拍照中
        //                            var a = Aoi1Message.Find(item =>
        //                                item[2] == 0 && item.Skip(34).Take(44 - 34).ToArray()
        //                                    .SequenceEqual(destination1)
        //                            );

        //                            if (a != null)
        //                            {
        //                                var re = new byte[value.Length];
        //                                Array.Copy(value, re, value.Length);
        //                                re[1] = 1;
        //                                re[2] = 0;
        //                                re[3] = 0;
        //                                _remoteUdp?.SendAsync(_plcIpEndPoint, re);
        //                                Logger?.Info("拍照中O->PLC:");
        //                                Logger?.Info(re);
        //                                Logger?.Info("-------------------------");
        //                                inPhoto = false;
        //                                Aoi1Message.RemoveAll(item => item.SequenceEqual(a));
        //                            }
        //                        }

        //                        lock ((Aoi1Message as ICollection).SyncRoot)
        //                        {
        //                            //拍照完成
        //                            var b = Aoi1Message.Find(item =>
        //                                item[2] == 1 && item.Skip(34).Take(44 - 34).ToArray()
        //                                    .SequenceEqual(destination1)
        //                            );
        //                            if (b != null)
        //                            {
        //                                var re = new byte[value.Length];
        //                                Array.Copy(value, re, value.Length);
        //                                re[1] = 1;
        //                                re[2] = 1;
        //                                re[3] = 0;
        //                                _remoteUdp?.SendAsync(_plcIpEndPoint, re);
        //                                Logger?.Info("拍照完成O->PLC:");
        //                                Logger?.Info(re);
        //                                Logger?.Info("-------------------------");
        //                                photoCompleted = false;
        //                                Aoi1Message.RemoveAll(item => item.SequenceEqual(b));
        //                            }
        //                        }

        //                        lock ((Aoi1Message as ICollection).SyncRoot)
        //                        {
        //                            //检测完成
        //                            var c = Aoi1Message.Find(item =>
        //                                item[2] == 2 && item.Skip(34).Take(44 - 34).ToArray()
        //                                    .SequenceEqual(destination1)
        //                            );
        //                            if (c != null)
        //                            {
        //                                var re = new byte[value.Length];
        //                                Array.Copy(value, re, value.Length);
        //                                re[1] = 1;
        //                                re[2] = 2;
        //                                re[3] = 0;
        //                                re[9] = c[9];
        //                                re[10] = c[10];
        //                                _remoteUdp?.SendAsync(_plcIpEndPoint, re);
        //                                Logger?.Info("发送结果O->PLC:");
        //                                Logger?.Info(re);
        //                                Logger?.Info("-------------------------");
        //                                complete = false;
        //                                Aoi1Message.RemoveAll(item => item.SequenceEqual(c));
        //                            }
        //                        }

        //                        var afterDt = DateTime.Now;
        //                        var ts = afterDt.Subtract(beforeDt);
        //                        timeOut = ts.Milliseconds;
        //                    }
        //                    RemoveQueue.Enqueue(value);
        //                    break;

        //                case 2:
        //                    var destination2 = value.Skip(44).Take(54 - 44).ToArray();
        //                    timeOut = 0;
        //                    beforeDt = DateTime.Now;
        //                    while ((inPhoto || photoCompleted || complete) && timeOut < 600)
        //                    {
        //                        lock ((Aoi2Message as ICollection).SyncRoot)
        //                        {
        //                            //拍照中
        //                            var a = Aoi2Message.Find(item =>
        //                                item[2] == 0 && item.Skip(34).Take(44 - 34).ToArray()
        //                                    .SequenceEqual(destination2)
        //                            );
        //                            if (a != null)
        //                            {
        //                                var re = new byte[value.Length];
        //                                Array.Copy(value, re, value.Length);
        //                                re[1] = 1;
        //                                re[2] = 0;
        //                                re[3] = 0;
        //                                _remoteUdp?.SendAsync(_plcIpEndPoint, re);
        //                                Logger?.Info("拍照中O->PLC:");
        //                                Logger?.Info(re);
        //                                Logger?.Info("-------------------------");
        //                                inPhoto = false;
        //                                Aoi2Message.RemoveAll(item => item.SequenceEqual(a));
        //                            }
        //                        }

        //                        lock ((Aoi2Message as ICollection).SyncRoot)
        //                        {
        //                            //拍照完成
        //                            var b = Aoi2Message.Find(item =>
        //                                item[2] == 1 && item.Skip(34).Take(44 - 34).ToArray()
        //                                    .SequenceEqual(destination2)
        //                            );
        //                            if (b != null)
        //                            {
        //                                var re = new byte[value.Length];
        //                                Array.Copy(value, re, value.Length);
        //                                re[1] = 1;
        //                                re[2] = 1;
        //                                re[3] = 0;
        //                                _remoteUdp?.SendAsync(_plcIpEndPoint, re);
        //                                Logger?.Info("拍照完成O->PLC:");
        //                                Logger?.Info(re);
        //                                Logger?.Info("-------------------------");
        //                                photoCompleted = false;
        //                                Aoi2Message.RemoveAll(item => item.SequenceEqual(b));
        //                            }
        //                        }

        //                        lock ((Aoi2Message as ICollection).SyncRoot)
        //                        {
        //                            //检测完成
        //                            var c = Aoi2Message.Find(item =>
        //                                item[2] == 2 && item.Skip(34).Take(44 - 34).ToArray()
        //                                    .SequenceEqual(destination2)
        //                            );
        //                            if (c != null)
        //                            {
        //                                var re = new byte[value.Length];
        //                                Array.Copy(value, re, value.Length);
        //                                re[1] = 1;
        //                                re[2] = 2;
        //                                re[3] = 0;
        //                                re[11] = c[9];
        //                                re[12] = c[10];
        //                                _remoteUdp?.SendAsync(_plcIpEndPoint, re);
        //                                Logger?.Info("发送结果O->PLC:");
        //                                Logger?.Info(re);
        //                                Logger?.Info("-------------------------");
        //                                complete = false;
        //                                Aoi2Message.RemoveAll(item => item.SequenceEqual(c));
        //                            }
        //                        }
        //                        var afterDt = DateTime.Now;
        //                        var ts = afterDt.Subtract(beforeDt);
        //                        timeOut = ts.Milliseconds;
        //                    }
        //                    RemoveQueue.Enqueue(value);
        //                    break;

        //                case 3:
        //                    var destination3 = value.Skip(34).Take(44 - 34).ToArray();
        //                    var destination4 = value.Skip(44).Take(54 - 44).ToArray();
        //                    timeOut = 0;
        //                    beforeDt = DateTime.Now;
        //                    while ((inPhoto || photoCompleted || complete) && timeOut < 600)
        //                    {
        //                        byte[] a = default;
        //                        byte[] a1 = default;
        //                        byte[] b = default;
        //                        byte[] b1 = default;
        //                        byte[] c = default;
        //                        byte[] c1 = default;

        //                        //拍照中
        //                        lock ((Aoi1Message as ICollection).SyncRoot)
        //                        {
        //                            a = Aoi1Message.Find(item => item[2] == 0 && item.Skip(34).Take(44 - 34).ToArray().SequenceEqual(destination3));
        //                        }
        //                        lock ((Aoi2Message as ICollection).SyncRoot)
        //                        {
        //                            a1 = Aoi2Message.Find(item => item[2] == 0 && item.Skip(34).Take(44 - 34).ToArray().SequenceEqual(destination4));
        //                        }
        //                        if (a != null && a1 != null)
        //                        {
        //                            var re = new byte[value.Length];
        //                            Array.Copy(value, re, value.Length);
        //                            re[1] = 1;
        //                            re[2] = 0;
        //                            re[3] = 0;
        //                            _remoteUdp?.SendAsync(_plcIpEndPoint, re);
        //                            Logger?.Info("拍照中O->PLC");
        //                            Logger?.Info(re);
        //                            Logger?.Info("-------------------------");
        //                            inPhoto = false;
        //                            lock ((Aoi1Message as ICollection).SyncRoot)
        //                            {
        //                                Aoi1Message.RemoveAll(item => item.SequenceEqual(a));
        //                            }

        //                            lock ((Aoi2Message as ICollection).SyncRoot)
        //                            {
        //                                Aoi2Message.RemoveAll(item => item.SequenceEqual(a1));
        //                            }
        //                        }

        //                        //拍照完成
        //                        lock ((Aoi1Message as ICollection).SyncRoot)
        //                        {
        //                            b = Aoi1Message.Find(item =>
        //                                item[2] == 1 && item.Skip(34).Take(44 - 34).ToArray()
        //                                    .SequenceEqual(destination3)
        //                            );
        //                        }
        //                        lock ((Aoi2Message as ICollection).SyncRoot)
        //                        {
        //                            b1 = Aoi2Message.Find(item =>
        //                                item[2] == 1 && item.Skip(34).Take(44 - 34).ToArray()
        //                                    .SequenceEqual(destination4)
        //                            );
        //                        }
        //                        if (b != null && b1 != null)
        //                        {
        //                            var re = new byte[value.Length];
        //                            Array.Copy(value, re, value.Length);
        //                            re[1] = 1;
        //                            re[2] = 1;
        //                            re[3] = 0;
        //                            _remoteUdp?.SendAsync(_plcIpEndPoint, re);
        //                            Logger?.Info("拍照完成O->PLC:");
        //                            Logger?.Info(re);
        //                            Logger?.Info("-------------------------");
        //                            photoCompleted = false;
        //                            lock ((Aoi1Message as ICollection).SyncRoot)
        //                            {
        //                                Aoi1Message.RemoveAll(item => item.SequenceEqual(b));
        //                            }
        //                            lock ((Aoi2Message as ICollection).SyncRoot)
        //                            {
        //                                Aoi2Message.RemoveAll(item => item.SequenceEqual(b1));
        //                            }
        //                        }

        //                        //检测完成
        //                        lock ((Aoi1Message as ICollection).SyncRoot)
        //                        {
        //                            c = Aoi1Message.Find(item =>
        //                                item[2] == 2 && item.Skip(34).Take(44 - 34).ToArray()
        //                                    .SequenceEqual(destination3)
        //                            );
        //                        }
        //                        lock ((Aoi2Message as ICollection).SyncRoot)
        //                        {
        //                            c1 = Aoi2Message.Find(item =>
        //                                item[2] == 2 && item.Skip(34).Take(44 - 34).ToArray()
        //                                    .SequenceEqual(destination4)
        //                            );
        //                        }
        //                        if (c != null && c1 != null)
        //                        {
        //                            var re = new byte[value.Length];
        //                            Array.Copy(value, re, value.Length);
        //                            re[1] = 1;
        //                            re[2] = 2;
        //                            re[3] = 0;
        //                            re[9] = c[9];
        //                            re[10] = c[10];
        //                            re[11] = c1[9];
        //                            re[12] = c1[10];
        //                            _remoteUdp?.SendAsync(_plcIpEndPoint, re);
        //                            Logger?.Info("发送结果O->PLC:");
        //                            Logger?.Info(re);
        //                            Logger?.Info("-------------------------");
        //                            complete = false;
        //                            lock ((Aoi1Message as ICollection).SyncRoot)
        //                            {
        //                                Aoi1Message.RemoveAll(item => item.SequenceEqual(c));
        //                            }
        //                            lock ((Aoi2Message as ICollection).SyncRoot)
        //                            {
        //                                Aoi2Message.RemoveAll(item => item.SequenceEqual(c1));
        //                            }
        //                        }

        //                        var afterDt = DateTime.Now;
        //                        var ts = afterDt.Subtract(beforeDt);
        //                        timeOut = ts.Milliseconds;
        //                    }
        //                    RemoveQueue.Enqueue(value);
        //                    break;

        //                default:
        //                    break;
        //            }
        //        }
        //        catch (Exception e)
        //        {
        //            Logger?.Error("返回消息给PLC出错:" + e.Message, e);
        //        }
        //    }
        //}

        #endregion 旧成品

        #region 旧出版

        /// <summary>
        /// 转发AOI发送的消息给PLC
        /// </summary>
        //private void CbTransmit()
        //{
        //    while (true)
        //    {
        //        try
        //        {
        //            long timeOut;
        //            DateTime beforeDt = default;
        //            //拍照中
        //            var inPhoto = true;
        //            //拍照完成
        //            var photoCompleted = true;
        //            //检测完成
        //            var complete = true;
        //            //收到的消息
        //            byte[] value = default;
        //            if (RemoteQueue == null || _plcIpEndPoint == null || RemoveQueue == null)
        //            {
        //                continue;
        //            }
        //            RemoteQueue.Dequeue(out value);
        //            Logger?.Info(value);
        //            switch (value[66])
        //            {
        //                case 1:
        //                    var destination1 = value.Skip(34).Take(44 - 34).ToArray();
        //                    timeOut = 0;
        //                    beforeDt = DateTime.Now;
        //                    while ((inPhoto || photoCompleted || complete) && timeOut < 600)
        //                    {
        //                        lock ((Aoi1Message as ICollection).SyncRoot)
        //                        {
        //                            //拍照中
        //                            var a = Aoi1Message.Find(item =>
        //                                item[2] == 0 && item.Skip(34).Take(44 - 34).ToArray()
        //                                    .SequenceEqual(destination1)
        //                            );
        //                            if (a != null)
        //                            {
        //                                var re = new byte[value.Length];
        //                                Array.Copy(value, re, value.Length);
        //                                re[1] = 1;
        //                                re[2] = 0;
        //                                re[3] = 0;
        //                                _remoteUdp?.SendAsync(_plcIpEndPoint, re);
        //                                Logger?.Info("拍照中O->PLC:");
        //                                Logger?.Info(re);
        //                                Logger?.Info("-------------------------");
        //                                inPhoto = false;
        //                                Aoi1Message.RemoveAll(item => item.SequenceEqual(a));
        //                            }
        //                        }

        //                        lock ((Aoi1Message as ICollection).SyncRoot)
        //                        {
        //                            //拍照完成
        //                            var b = Aoi1Message.Find(item =>
        //                                item[2] == 1 && item.Skip(34).Take(44 - 34).ToArray()
        //                                    .SequenceEqual(destination1)
        //                            );
        //                            if (b != null)
        //                            {
        //                                var re = new byte[value.Length];
        //                                Array.Copy(value, re, value.Length);
        //                                re[1] = 1;
        //                                re[2] = 1;
        //                                re[3] = 0;
        //                                _remoteUdp?.SendAsync(_plcIpEndPoint, re);
        //                                Logger?.Info("拍照完成O->PLC:");
        //                                Logger?.Info(re);
        //                                Logger?.Info("-------------------------");
        //                                photoCompleted = false;
        //                                Aoi1Message.RemoveAll(item => item.SequenceEqual(b));
        //                            }
        //                        }

        //                        lock ((Aoi1Message as ICollection).SyncRoot)
        //                        {
        //                            //检测完成
        //                            var c = Aoi1Message.Find(item =>
        //                                item[2] == 2 && item.Skip(34).Take(44 - 34).ToArray()
        //                                    .SequenceEqual(destination1)
        //                            );
        //                            if (c != null)
        //                            {
        //                                var re = new byte[value.Length];
        //                                Array.Copy(value, re, value.Length);
        //                                re[1] = 1;
        //                                re[2] = 2;
        //                                re[3] = 0;
        //                                if (c[11] == 2)
        //                                {
        //                                    re[9] = 2;
        //                                }
        //                                else
        //                                {
        //                                    re[9] = 1;
        //                                }

        //                                //re[9] = c[9];
        //                                re[12] = c[12];
        //                                _remoteUdp?.SendAsync(_plcIpEndPoint, re);
        //                                Logger?.Info("发送结果O->PLC:");
        //                                Logger?.Info(re);
        //                                Logger?.Info("-------------------------");
        //                                complete = false;
        //                                Aoi1Message.RemoveAll(item => item.SequenceEqual(c));
        //                            }
        //                        }

        //                        var afterDt = DateTime.Now;
        //                        var ts = afterDt.Subtract(beforeDt);
        //                        timeOut = ts.Milliseconds;
        //                    }
        //                    RemoveQueue.Enqueue(value);
        //                    break;

        //                case 2:
        //                    var destination2 = value.Skip(44).Take(54 - 44).ToArray();
        //                    timeOut = 0;
        //                    beforeDt = DateTime.Now;
        //                    while ((inPhoto || photoCompleted || complete) && timeOut < 600)
        //                    {
        //                        lock ((Aoi2Message as ICollection).SyncRoot)
        //                        {
        //                            //拍照中
        //                            var a = Aoi2Message.Find(item =>
        //                                item[2] == 0 && item.Skip(34).Take(44 - 34).ToArray()
        //                                    .SequenceEqual(destination2)
        //                            );
        //                            if (a != null)
        //                            {
        //                                var re = new byte[value.Length];
        //                                Array.Copy(value, re, value.Length);
        //                                re[1] = 1;
        //                                re[2] = 0;
        //                                re[3] = 0;
        //                                _remoteUdp?.SendAsync(_plcIpEndPoint, re);
        //                                Logger?.Info("拍照中O->PLC:");
        //                                Logger?.Info(re);
        //                                Logger?.Info("-------------------------");
        //                                inPhoto = false;
        //                                Aoi2Message.RemoveAll(item => item.SequenceEqual(a));
        //                            }
        //                        }

        //                        lock ((Aoi2Message as ICollection).SyncRoot)
        //                        {
        //                            //拍照完成
        //                            var b = Aoi2Message.Find(item =>
        //                                item[2] == 1 && item.Skip(34).Take(44 - 34).ToArray()
        //                                    .SequenceEqual(destination2)
        //                            );
        //                            if (b != null)
        //                            {
        //                                var re = new byte[value.Length];
        //                                Array.Copy(value, re, value.Length);
        //                                re[1] = 1;
        //                                re[2] = 1;
        //                                re[3] = 0;
        //                                _remoteUdp?.SendAsync(_plcIpEndPoint, re);
        //                                Logger?.Info("拍照完成O->PLC:");
        //                                Logger?.Info(re);
        //                                Logger?.Info("-------------------------");
        //                                photoCompleted = false;
        //                                Aoi2Message.RemoveAll(item => item.SequenceEqual(b));
        //                            }
        //                        }
        //                        lock ((Aoi2Message as ICollection).SyncRoot)
        //                        {
        //                            //检测完成
        //                            var c = Aoi2Message.Find(item =>
        //                                item[2] == 2 && item.Skip(34).Take(44 - 34).ToArray().SequenceEqual(destination2)
        //                            );
        //                            if (c != null)
        //                            {
        //                                var re = new byte[value.Length];
        //                                Array.Copy(value, re, value.Length);
        //                                re[1] = 1;
        //                                re[2] = 2;
        //                                re[3] = 0;
        //                                if (c[11] == 2)
        //                                {
        //                                    re[9] = 2;
        //                                }
        //                                else
        //                                {
        //                                    re[9] = 1;
        //                                }
        //                                re[11] = c[12];
        //                                //re[12] = c[11];
        //                                _remoteUdp?.SendAsync(_plcIpEndPoint, re);
        //                                Logger?.Info("发送结果O->PLC:");
        //                                Logger?.Info(re);
        //                                Logger?.Info("-------------------------");
        //                                complete = false;
        //                                Aoi2Message.RemoveAll(item => item.SequenceEqual(c));
        //                            }
        //                        }
        //                        var afterDt = DateTime.Now;
        //                        var ts = afterDt.Subtract(beforeDt);
        //                        timeOut = ts.Milliseconds;
        //                    }
        //                    RemoveQueue.Enqueue(value);
        //                    break;

        //                case 3:
        //                    var destination3 = value.Skip(34).Take(44 - 34).ToArray();
        //                    var destination4 = value.Skip(44).Take(54 - 44).ToArray();
        //                    timeOut = 0;
        //                    beforeDt = DateTime.Now;
        //                    while ((inPhoto || photoCompleted || complete) && timeOut < 600)
        //                    {
        //                        byte[] a = default;
        //                        byte[] a1 = default;
        //                        byte[] b = default;
        //                        byte[] b1 = default;
        //                        byte[] c = default;
        //                        byte[] c1 = default;

        //                        //拍照中
        //                        lock ((Aoi1Message as ICollection).SyncRoot)
        //                        {
        //                            a = Aoi1Message.Find(item =>
        //                                item[2] == 0 && item.Skip(34).Take(44 - 34).ToArray()
        //                                    .SequenceEqual(destination3)
        //                            );
        //                        }
        //                        lock ((Aoi2Message as ICollection).SyncRoot)
        //                        {
        //                            a1 = Aoi2Message.Find(item =>
        //                                item[2] == 0 && item.Skip(34).Take(44 - 34).ToArray()
        //                                    .SequenceEqual(destination4)
        //                            );
        //                        }
        //                        if (a != null && a1 != null)
        //                        {
        //                            var re = new byte[value.Length];
        //                            Array.Copy(value, re, value.Length);
        //                            re[1] = 1;
        //                            re[2] = 0;
        //                            re[3] = 0;
        //                            _remoteUdp?.SendAsync(_plcIpEndPoint, re);
        //                            Logger?.Info("拍照中O->PLC");
        //                            Logger?.Info(re);
        //                            Logger?.Info("-------------------------");
        //                            inPhoto = false;
        //                            lock ((Aoi1Message as ICollection).SyncRoot)
        //                            {
        //                                Aoi1Message.RemoveAll(item => item.SequenceEqual(a));
        //                            }
        //                            lock ((Aoi2Message as ICollection).SyncRoot)
        //                            {
        //                                Aoi2Message.RemoveAll(item => item.SequenceEqual(a1));
        //                            }
        //                        }

        //                        //拍照完成
        //                        lock ((Aoi1Message as ICollection).SyncRoot)
        //                        {
        //                            b = Aoi1Message.Find(item =>
        //                               item[2] == 1 && item.Skip(34).Take(44 - 34).ToArray().SequenceEqual(destination3)
        //                           );
        //                        }
        //                        lock ((Aoi2Message as ICollection).SyncRoot)
        //                        {
        //                            b1 = Aoi2Message.Find(item =>
        //                                item[2] == 1 && item.Skip(34).Take(44 - 34).ToArray().SequenceEqual(destination4)
        //                            );
        //                        }
        //                        if (b != null && b1 != null)
        //                        {
        //                            var re = new byte[value.Length];
        //                            Array.Copy(value, re, value.Length);
        //                            re[1] = 1;
        //                            re[2] = 1;
        //                            re[3] = 0;
        //                            _remoteUdp?.SendAsync(_plcIpEndPoint, re);
        //                            Logger?.Info("拍照完成O->PLC:");
        //                            Logger?.Info(re);
        //                            Logger?.Info("-------------------------");
        //                            photoCompleted = false;
        //                            lock ((Aoi1Message as ICollection).SyncRoot)
        //                            {
        //                                Aoi1Message.RemoveAll(item => item.SequenceEqual(b));
        //                            }
        //                            lock ((Aoi2Message as ICollection).SyncRoot)
        //                            {
        //                                Aoi2Message.RemoveAll(item => item.SequenceEqual(b1));
        //                            }
        //                        }

        //                        //检测完成
        //                        lock ((Aoi1Message as ICollection).SyncRoot)
        //                        {
        //                            c = Aoi1Message.Find(item =>
        //                                item[2] == 2 && item.Skip(34).Take(44 - 34).ToArray().SequenceEqual(destination3)
        //                            );
        //                        }
        //                        lock ((Aoi2Message as ICollection).SyncRoot)
        //                        {
        //                            c1 = Aoi2Message.Find(item =>
        //                                item[2] == 2 && item.Skip(34).Take(44 - 34).ToArray().SequenceEqual(destination4)
        //                            );
        //                        }
        //                        if (c != null && c1 != null)
        //                        {
        //                            var re = new byte[value.Length];
        //                            Array.Copy(value, re, value.Length);
        //                            re[1] = 1;
        //                            re[2] = 2;
        //                            re[3] = 0;
        //                            if (c[11] == 2 || c1[11] == 2)
        //                            {
        //                                re[9] = 2;
        //                            }
        //                            else
        //                            {
        //                                re[9] = 1;
        //                            }
        //                            //re[10] = c[10];
        //                            re[11] = c1[12];
        //                            re[12] = c[12];
        //                            _remoteUdp?.SendAsync(_plcIpEndPoint, re);
        //                            Logger?.Info("发送结果O->PLC:");
        //                            Logger?.Info(re);
        //                            Logger?.Info("-------------------------");
        //                            complete = false;
        //                            lock ((Aoi1Message as ICollection).SyncRoot)
        //                            {
        //                                Aoi1Message.RemoveAll(item => item.SequenceEqual(c));
        //                            }
        //                            lock ((Aoi2Message as ICollection).SyncRoot)
        //                            {
        //                                Aoi2Message.RemoveAll(item => item.SequenceEqual(c1));
        //                            }
        //                        }
        //                        var afterDt = DateTime.Now;
        //                        var ts = afterDt.Subtract(beforeDt);
        //                        timeOut = ts.Milliseconds;
        //                    }
        //                    RemoveQueue.Enqueue(value);
        //                    break;

        //                default:
        //                    break;
        //            }
        //        }
        //        catch (Exception e)
        //        {
        //            Logger?.Error("返回消息给PLC出错:" + e.Message, e);
        //        }
        //    }
        //}

        #endregion 旧出版

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
                    //拍照中
                    var inPhoto = false;
                    //拍照完成
                    var photoCompleted = true;
                    //检测完成
                    var complete = true;
                    //收到的消息
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
                            var destination1 = value.Bytes1.Skip(34).Take(44 - 34).ToArray();
                            timeOut = 0;
                            beforeDt = DateTime.Now;

                            while ((inPhoto || photoCompleted || complete) && timeOut < _timeout)
                            {
                                //拍照中
                                //LockMethod(() =>
                                //{
                                //    var a = Aoi1Message.Find(item =>
                                //        item[2] == 0 && item.Skip(34).Take(44 - 34).ToArray()
                                //            .SequenceEqual(destination1)
                                //    );

                                //    if (a != null)
                                //    {
                                //        var re = new byte[value.Length];
                                //        Array.Copy(value, re, value.Length);
                                //        re[1] = 1;
                                //        re[2] = 0;
                                //        re[3] = 0;
                                //        _remoteUdp?.SendAsync(_plcIpEndPoint, re);
                                //        Logger?.Info($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}拍照中O->PLC:");
                                //        Logger?.Info(re);
                                //        Logger?.Info("----------------------------------------------------");
                                //        inPhoto = false;
                                //        Aoi1Message.RemoveAll(item => item.SequenceEqual(a));
                                //    }
                                //});

                                //拍照完成
                                //LockMethod(() =>
                                //{
                                var b = Aoi1Message.Where(item => item.Key[2] == 1 && item.Value.Skip(34).Take(44 - 34).ToArray()
                                            .SequenceEqual(destination1)).FirstOrDefault();
                                //var b = Aoi1Message.Find(item =>
                                //    item[2] == 1 && item.Skip(34).Take(44 - 34).ToArray()
                                //        .SequenceEqual(destination1)
                                //);
                                if (!b.Equals(default(KeyValuePair<byte[], byte[]>)))
                                {
                                    var re = new byte[value.BytesOriginal.Length];
                                    Array.Copy(value.BytesOriginal, re, value.BytesOriginal.Length);
                                    re[1] = 1;
                                    re[2] = 1;
                                    re[3] = 0;
                                    _remoteUdp?.Send(_plcIpEndPoint, re);
                                    Logger?.Info($"{label19.Text}-{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}拍照完成O->PLC:");
                                    //Logger?.Info(re);
                                    //Logger?.Info("----------------------------------------------------");
                                    photoCompleted = false;
                                    // Aoi1Message.RemoveAll(item => item.SequenceEqual(b));
                                    Aoi1Message.TryRemove(b.Key, out _);
                                }
                                //});

                                //检测完成
                                //LockMethod(() =>
                                //{
                                var c = Aoi1Message.Where(item => item.Key[2] == 2 && item.Value.Skip(34).Take(44 - 34).ToArray()
                                      .SequenceEqual(destination1)).FirstOrDefault();
                                // var c = Aoi1Message.Find(item =>
                                //    item[2] == 2 && item.Skip(34).Take(44 - 34).ToArray()
                                //        .SequenceEqual(destination1)
                                //);
                                if (!c.Equals(default(KeyValuePair<byte[], byte[]>)))
                                {
                                    var re = new byte[value.BytesOriginal.Length];
                                    Array.Copy(value.BytesOriginal, re, value.BytesOriginal.Length);
                                    re[1] = 1;
                                    re[2] = 2;
                                    re[3] = 0;
                                    re[9] = c.Value[9];
                                    re[10] = c.Value[10];
                                    re[89] = c.Value[11];
                                    re[90] = c.Value[90];
                                    _remoteUdp?.Send(_plcIpEndPoint, re);
                                    Logger?.Info($"{label19.Text}-{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}发送结果O->PLC:{c.Value[9]},{c.Value[10]}");
                                    //Logger?.Info(re);
                                    //Logger?.Info("----------------------------------------------------");
                                    complete = false;
                                    //Aoi1Message.RemoveAll(item => item.SequenceEqual(c));
                                    Aoi1Message.TryRemove(c.Key, out _);
                                }
                                //});

                                var afterDt = DateTime.Now;
                                var ts = afterDt.Subtract(beforeDt);
                                timeOut = ts.Ticks / 10000;
                            }
                            //RemoveQueue.Enqueue(value);
                            break;

                        case 2:
                            var destination2 = value.Bytes2.Skip(34).Take(44 - 34).ToArray();
                            timeOut = 0;
                            beforeDt = DateTime.Now;
                            while ((inPhoto || photoCompleted || complete) && timeOut < _timeout)
                            {
                                //拍照中
                                //LockMethod1(() =>
                                //{
                                //    var a = Aoi2Message.Find(item =>
                                //        item[2] == 0 && item.Skip(34).Take(44 - 34).ToArray()
                                //            .SequenceEqual(destination2)
                                //    );
                                //    if (a != null)
                                //    {
                                //        var re = new byte[value.Length];
                                //        Array.Copy(value, re, value.Length);
                                //        re[1] = 1;
                                //        re[2] = 0;
                                //        re[3] = 0;
                                //        _remoteUdp?.SendAsync(_plcIpEndPoint, re);
                                //        Logger?.Info($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}拍照中O->PLC:");
                                //        Logger?.Info(re);
                                //        Logger?.Info("----------------------------------------------------");
                                //        inPhoto = false;
                                //        Aoi2Message.RemoveAll(item => item.SequenceEqual(a));
                                //    }
                                //});

                                //拍照完成
                                //LockMethod1(() =>
                                //{
                                var b = Aoi2Message.Where(item => item.Key[2] == 1 && item.Value.Skip(34).Take(44 - 34).ToArray()
                                       .SequenceEqual(destination2)).FirstOrDefault();
                                //var b = Aoi2Message.Find(item =>
                                //    item[2] == 1 && item.Skip(34).Take(44 - 34).ToArray()
                                //        .SequenceEqual(destination2)
                                //);
                                if (!b.Equals(default(KeyValuePair<byte[], byte[]>)))
                                {
                                    var re = new byte[value.BytesOriginal.Length];
                                    Array.Copy(value.BytesOriginal, re, value.BytesOriginal.Length);
                                    re[1] = 1;
                                    re[2] = 1;
                                    re[3] = 0;
                                    _remoteUdp?.Send(_plcIpEndPoint, re);
                                    Logger?.Info($"{label19.Text}-{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}拍照完成O->PLC:");
                                    //Logger?.Info(re);
                                    //Logger?.Info("----------------------------------------------------");
                                    photoCompleted = false;
                                    //Aoi2Message.RemoveAll(item => item.SequenceEqual(b));
                                    Aoi2Message.TryRemove(b.Key, out _);
                                }
                                //});

                                //检测完成
                                //LockMethod1(() =>
                                //{
                                var c = Aoi2Message.Where(item => item.Key[2] == 2 && item.Value.Skip(34).Take(44 - 34).ToArray()
                                          .SequenceEqual(destination2)).FirstOrDefault();
                                //var c = Aoi2Message.Find(item =>
                                //    item[2] == 2 && item.Skip(34).Take(44 - 34).ToArray()
                                //        .SequenceEqual(destination2)
                                //);
                                if (!c.Equals(default(KeyValuePair<byte[], byte[]>)))
                                {
                                    var re = new byte[value.BytesOriginal.Length];
                                    Array.Copy(value.BytesOriginal, re, value.BytesOriginal.Length);
                                    re[1] = 1;
                                    re[2] = 2;
                                    re[3] = 0;
                                    re[11] = c.Value[9];
                                    re[12] = c.Value[10];
                                    re[89] = c.Value[11];
                                    re[90] = c.Value[90];
                                    _remoteUdp?.Send(_plcIpEndPoint, re);
                                    Logger?.Info($"{label19.Text}-{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}发送结果O->PLC:{c.Value[9]},{c.Value[10]}");
                                    //Logger?.Info(re); 
                                    //Logger?.Info("----------------------------------------------------");
                                    complete = false;
                                    //Aoi2Message.RemoveAll(item => item.SequenceEqual(c));
                                    Aoi2Message.TryRemove(c.Key, out _);
                                }
                                //});

                                var afterDt = DateTime.Now;
                                var ts = afterDt.Subtract(beforeDt);
                                timeOut = ts.Ticks / 10000;
                            }
                            //RemoveQueue.Enqueue(value);
                            break;

                        case 3:
                            var destination3 = value.Bytes1.Skip(34).Take(44 - 34).ToArray();
                            var destination4 = value.Bytes2.Skip(34).Take(44 - 34).ToArray();
                            timeOut = 0;
                            beforeDt = DateTime.Now;
                            KeyValuePair<byte[], byte[]> bb = default;
                            KeyValuePair<byte[], byte[]> bb1 = default;
                            KeyValuePair<byte[], byte[]> cc = default;
                            KeyValuePair<byte[], byte[]> cc1 = default;
                            var isSenndTakePhoto = false;
                            var isSenndResult = true;
                            while ((inPhoto || photoCompleted || complete) && timeOut < _timeout)
                            {
                                //byte[] a = default;
                                //byte[] a1 = default;

                                //KeyValuePair<byte[], byte[]> b = default;
                                //KeyValuePair<byte[], byte[]> b1 = default;
                                //KeyValuePair<byte[], byte[]> c = default;
                                //KeyValuePair<byte[], byte[]> c1 = default;

                                //拍照中
                                //LockMethod(() =>
                                //{
                                //    a = Aoi1Message.Find(item => item[2] == 0 && item.Skip(34).Take(44 - 34).ToArray().SequenceEqual(destination3));
                                //});

                                //LockMethod1(() =>
                                //{
                                //    a1 = Aoi2Message.Find(item => item[2] == 0 && item.Skip(34).Take(44 - 34).ToArray().SequenceEqual(destination4));
                                //});

                                //if (a != null && a1 != null)
                                //{
                                //    var re = new byte[value.Length];
                                //    Array.Copy(value, re, value.Length);
                                //    re[1] = 1;
                                //    re[2] = 0;
                                //    re[3] = 0;
                                //    _remoteUdp?.SendAsync(_plcIpEndPoint, re);
                                //    Logger?.Info($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}拍照中O->PLC");
                                //    Logger?.Info(re);
                                //    Logger?.Info("----------------------------------------------------");
                                //    inPhoto = false;

                                //    LockMethod(() => { Aoi1Message.RemoveAll(item => item.SequenceEqual(a)); });
                                //    LockMethod1(() => { Aoi2Message.RemoveAll(item => item.SequenceEqual(a1)); });
                                //}

                                //拍照完成
                                //LockMethod(() =>
                                //{
                                bb = Aoi1Message.Where(item =>
                                item.Key[2] == 1 && item.Value.Skip(34).Take(44 - 34).ToArray()
                                   .SequenceEqual(destination3)).FirstOrDefault();

                                //   b = Aoi1Message.Find(item =>
                                //    item[2] == 1 && item.Skip(34).Take(44 - 34).ToArray()
                                //        .SequenceEqual(destination3)
                                //);
                                //});

                                //LockMethod1(() =>
                                //{
                                bb1 = Aoi2Message.Where(item =>
                                item.Key[2] == 1 && item.Value.Skip(34).Take(44 - 34).ToArray()
                                .SequenceEqual(destination4)).FirstOrDefault();

                                //    b1 = Aoi2Message.Find(item =>
                                //    item[2] == 1 && item.Skip(34).Take(44 - 34).ToArray()
                                //        .SequenceEqual(destination4)
                                //);
                                //});

                                if (!bb.Equals(default(KeyValuePair<byte[], byte[]>)) && !bb1.Equals(default(KeyValuePair<byte[], byte[]>)))
                                {
                                    var re = new byte[value.BytesOriginal.Length];
                                    Array.Copy(value.BytesOriginal, re, value.BytesOriginal.Length);
                                    re[1] = 1;
                                    re[2] = 1;
                                    re[3] = 0;
                                    _remoteUdp?.Send(_plcIpEndPoint, re);
                                    isSenndTakePhoto = true;
                                    Logger?.Info($"{label19.Text}-{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}拍照完成O->PLC:");
                                    //Logger?.Info(re);
                                    //Logger?.Info("----------------------------------------------------");
                                    photoCompleted = false;

                                    //LockMethod(() => {
                                    //Aoi1Message.RemoveAll(item => item.SequenceEqual(b));
                                    Aoi1Message.TryRemove(bb.Key, out _);
                                    //});
                                    //LockMethod1(() =>
                                    //{
                                    //Aoi2Message.RemoveAll(item => item.SequenceEqual(b1));
                                    Aoi2Message.TryRemove(bb1.Key, out _);
                                    //});
                                }

                                //检测完成
                                //LockMethod(() =>
                                //{
                                cc = Aoi1Message.Where(item =>
                             item.Key[2] == 2 && item.Value.Skip(34).Take(44 - 34).ToArray()
                                 .SequenceEqual(destination3)
                               ).FirstOrDefault();
                                //    c = Aoi1Message.Find(item =>
                                //    item[2] == 2 && item.Skip(34).Take(44 - 34).ToArray()
                                //        .SequenceEqual(destination3)
                                //);
                                //});

                                //LockMethod1(() =>
                                //{
                                cc1 = Aoi2Message.Where(item =>
                            item.Key[2] == 2 && item.Value.Skip(34).Take(44 - 34).ToArray()
                                .SequenceEqual(destination4)
                        ).FirstOrDefault();
                                //    c1 = Aoi2Message.Find(item =>
                                //    item[2] == 2 && item.Skip(34).Take(44 - 34).ToArray()
                                //        .SequenceEqual(destination4)
                                //);
                                //});

                                if (!cc.Equals(default(KeyValuePair<byte[], byte[]>)) && !cc1.Equals(default(KeyValuePair<byte[], byte[]>)))
                                {
                                    var re = new byte[value.BytesOriginal.Length];
                                    Array.Copy(value.BytesOriginal, re, value.BytesOriginal.Length);
                                    re[1] = 1;
                                    re[2] = 2;
                                    re[3] = 0;
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
                                    //Logger?.Info(re);
                                    //Logger?.Info("----------------------------------------------------");
                                    complete = false;

                                    //LockMethod(() => {
                                    //Aoi1Message.RemoveAll(item => item.SequenceEqual(c));
                                    Aoi1Message.TryRemove(cc.Key, out _);
                                    //});
                                    //LockMethod1(() => {
                                    //Aoi2Message.RemoveAll(item => item.SequenceEqual(c1));
                                    Aoi2Message.TryRemove(cc1.Key, out _);
                                    //});
                                }

                                var afterDt = DateTime.Now;
                                var ts = afterDt.Subtract(beforeDt);
                                timeOut = ts.Ticks / 10000;
                            }
                            if (isSenndTakePhoto && isSenndResult)
                            {
                                if (!cc.Equals(default(KeyValuePair<byte[], byte[]>)) && cc1.Equals(default(KeyValuePair<byte[], byte[]>)))
                                {
                                    var re = new byte[value.BytesOriginal.Length];
                                    Array.Copy(value.BytesOriginal, re, value.BytesOriginal.Length);
                                    re[1] = 1;
                                    re[2] = 2;
                                    re[3] = 0;
                                    switch (value.ty)
                                    {
                                        case 1:
                                            re[9] = cc.Value[9];
                                            re[10] = cc.Value[10];
                                            break;

                                        case 2:
                                            re[11] = WaiGuan;
                                            re[12] = Color;
                                            break;

                                        case 3:
                                            re[9] = cc.Value[9];
                                            re[10] = cc.Value[10];
                                            re[11] = WaiGuan;
                                            re[12] = Color;
                                            break;
                                    }
                                    _remoteUdp?.Send(_plcIpEndPoint, re);
                                    Logger?.Info($"{label19.Text}-{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}发送结果O->PLC:{cc.Value[9]},{cc.Value[10]},{WaiGuan},{Color}");
                                    //Logger?.Info(re);
                                    //Logger?.Info("----------------------------------------------------");
                                    complete = false;

                                    Aoi1Message.TryRemove(cc.Key, out _);
                                }
                                else if (cc.Equals(default(KeyValuePair<byte[], byte[]>)) && !cc1.Equals(default(KeyValuePair<byte[], byte[]>)))
                                {
                                    var re = new byte[value.BytesOriginal.Length];
                                    Array.Copy(value.BytesOriginal, re, value.BytesOriginal.Length);
                                    re[1] = 1;
                                    re[2] = 2;
                                    re[3] = 0;
                                    switch (value.ty)
                                    {
                                        case 1:
                                            re[9] = WaiGuan;
                                            re[10] = Color;
                                            break;

                                        case 2:
                                            re[11] = cc1.Value[9];
                                            re[12] = cc1.Value[10];
                                            break;

                                        case 3:
                                            re[9] = WaiGuan;
                                            re[10] = Color;
                                            re[11] = cc1.Value[9];
                                            re[12] = cc1.Value[10];
                                            break;
                                    }

                                    _remoteUdp?.Send(_plcIpEndPoint, re);
                                    Logger?.Info($"{label19.Text}-{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}发送结果O->PLC:{WaiGuan},{Color},{cc1.Value[9]},{cc1.Value[10]}");
                                    //Logger?.Info(re);
                                    //Logger?.Info("----------------------------------------------------");
                                    complete = false;
                                    Aoi2Message.TryRemove(cc1.Key, out _);
                                }
                                else
                                {
                                    var re = new byte[value.BytesOriginal.Length];
                                    Array.Copy(value.BytesOriginal, re, value.BytesOriginal.Length);
                                    re[1] = 1;
                                    re[2] = 2;
                                    re[3] = 0;
                                    switch (value.ty)
                                    {
                                        case 1:
                                            re[9] = WaiGuan;
                                            re[10] = Color;
                                            break;

                                        case 2:
                                            re[11] = WaiGuan;
                                            re[12] = Color;
                                            break;

                                        case 3:
                                            re[9] = WaiGuan;
                                            re[10] = Color;
                                            re[11] = WaiGuan;
                                            re[12] = Color;
                                            break;
                                    }

                                    _remoteUdp?.Send(_plcIpEndPoint, re);
                                    Logger?.Info($"{label19.Text}-{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}发送结果O->PLC:{WaiGuan},{Color},{WaiGuan},{Color}");
                                    //Logger?.Info(re);
                                    //Logger?.Info("----------------------------------------------------");
                                    complete = false;
                                    //Aoi2Message.TryRemove(cc1.Key, out _);
                                }
                            }
                            //RemoveQueue.Enqueue(value);
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
                    //拍照中
                    var inPhoto = false;
                    //拍照完成
                    var photoCompleted = true;
                    //检测完成
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
                            var destination1 = value.Bytes1.Skip(34).Take(44 - 34).ToArray();
                            timeOut = 0;
                            beforeDt = DateTime.Now;
                            while ((inPhoto || photoCompleted || complete) && timeOut < _timeout)
                            {
                                //拍照中
                                //LockMethod(() =>
                                //{
                                //    var a = Aoi1Message.Find(item =>
                                //        item[2] == 0 && item.Skip(34).Take(44 - 34).ToArray()
                                //            .SequenceEqual(destination1)
                                //    );
                                //    if (a != null)
                                //    {
                                //        var re = new byte[value.Length];
                                //        Array.Copy(value, re, value.Length);
                                //        re[1] = 1;
                                //        re[2] = 0;
                                //        re[3] = 0;
                                //        _remoteUdp?.SendAsync(_plcIpEndPoint, re);
                                //        Logger?.Info($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}拍照中O->PLC:");
                                //        Logger?.Info(re);
                                //        Logger?.Info("----------------------------------------------------");
                                //        inPhoto = false;
                                //        Aoi1Message.RemoveAll(item => item.SequenceEqual(a));
                                //    }
                                //});

                                //拍照完成
                                //LockMethod(() =>
                                //{
                                var b = Aoi1Message.Where(item =>
                                   item.Key[2] == 1 && item.Value.Skip(34).Take(44 - 34).ToArray()
                                       .SequenceEqual(destination1)
                               ).FirstOrDefault();
                                //var b = Aoi1Message.Find(item =>
                                //    item[2] == 1 && item.Skip(34).Take(44 - 34).ToArray()
                                //        .SequenceEqual(destination1)
                                //);
                                if (!b.Equals(default(KeyValuePair<byte[], byte[]>)))
                                {
                                    var re = new byte[value.BytesOriginal.Length];
                                    Array.Copy(value.BytesOriginal, re, value.BytesOriginal.Length);
                                    re[1] = 1;
                                    re[2] = 1;
                                    re[3] = 0;
                                    _remoteUdp?.SendAsync(_plcIpEndPoint, re);
                                    Logger?.Info($"{label19.Text}-{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}拍照完成O->PLC:");
                                    Logger?.Info(re);
                                    Logger?.Info("----------------------------------------------------");
                                    photoCompleted = false;
                                    //Aoi1Message.RemoveAll(item => item.SequenceEqual(b));
                                    Aoi1Message.TryRemove(b.Key, out _);
                                }
                                //});

                                //检测完成
                                //LockMethod(() =>
                                //{
                                var c = Aoi1Message.Where(item =>
                                    item.Key[2] == 2 && item.Value.Skip(34).Take(44 - 34).ToArray()
                                        .SequenceEqual(destination1)
                                ).FirstOrDefault();
                                //var c = Aoi1Message.Find(item =>
                                //    item[2] == 2 && item.Skip(34).Take(44 - 34).ToArray()
                                //        .SequenceEqual(destination1)
                                //);
                                if (!c.Equals(default(KeyValuePair<byte[], byte[]>)))
                                {
                                    var re = new byte[value.BytesOriginal.Length];
                                    Array.Copy(value.BytesOriginal, re, value.BytesOriginal.Length);
                                    re[1] = 1;
                                    re[2] = 2;
                                    re[3] = 0;

                                    if (c.Value[11] == 2)
                                    {
                                        re[13] = 2;
                                    }
                                    else
                                    {
                                        re[13] = 1;
                                    }
                                    //re[9] = c[9];
                                    re[9] = c.Value[12];

                                    var re2 = re.Take(90);
                                    var waferData = c.Value.Skip(90).Take(value.BytesOriginal.Length - 90);

                                    _remoteUdp?.SendAsync(_plcIpEndPoint, re2.Concat(waferData).ToArray());
                                    Logger?.Info($"{label19.Text}-{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}发送结果O->PLC:");
                                    Logger?.Info(re);
                                    Logger?.Info("----------------------------------------------------");
                                    complete = false;
                                    //Aoi1Message.RemoveAll(item => item.SequenceEqual(c));
                                    Aoi1Message.TryRemove(c.Key, out _);
                                }
                                //});

                                var afterDt = DateTime.Now;
                                var ts = afterDt.Subtract(beforeDt);
                                timeOut = ts.Ticks / 10000;
                            }
                            //RemoveQueue.Enqueue(value);
                            break;

                        case 2:
                            var destination2 = value.Bytes2.Skip(34).Take(44 - 34).ToArray();
                            timeOut = 0;
                            beforeDt = DateTime.Now;
                            while ((inPhoto || photoCompleted || complete) && timeOut < _timeout)
                            {
                                //拍照中
                                //LockMethod1(() =>
                                //{
                                //    var a = Aoi2Message.Find(item =>
                                //        item[2] == 0 && item.Skip(34).Take(44 - 34).ToArray()
                                //            .SequenceEqual(destination2)
                                //    );
                                //    if (a != null)
                                //    {
                                //        var re = new byte[value.Length];
                                //        Array.Copy(value, re, value.Length);
                                //        re[1] = 1;
                                //        re[2] = 0;
                                //        re[3] = 0;
                                //        _remoteUdp?.SendAsync(_plcIpEndPoint, re);
                                //        Logger?.Info($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}拍照中O->PLC:");
                                //        Logger?.Info(re);
                                //        Logger?.Info("----------------------------------------------------");
                                //        inPhoto = false;
                                //        Aoi2Message.RemoveAll(item => item.SequenceEqual(a));
                                //    }
                                //});

                                //拍照完成
                                //LockMethod1(() =>
                                //{
                                var b = Aoi2Message.Where(item =>
                           item.Key[2] == 1 && item.Value.Skip(34).Take(44 - 34).ToArray()
                               .SequenceEqual(destination2)
                             ).FirstOrDefault();
                                //    var b = Aoi2Message.Find(item =>
                                //    item[2] == 1 && item.Skip(34).Take(44 - 34).ToArray()
                                //        .SequenceEqual(destination2)
                                //);
                                if (!b.Equals(default(KeyValuePair<byte[], byte[]>)))
                                {
                                    var re = new byte[value.BytesOriginal.Length];
                                    Array.Copy(value.BytesOriginal, re, value.BytesOriginal.Length);
                                    re[1] = 1;
                                    re[2] = 1;
                                    re[3] = 0;
                                    _remoteUdp?.SendAsync(_plcIpEndPoint, re);
                                    Logger?.Info($"{label19.Text}-{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}拍照完成O->PLC:");
                                    Logger?.Info(re);
                                    Logger?.Info("----------------------------------------------------");
                                    photoCompleted = false;
                                    //Aoi2Message.RemoveAll(item => item.SequenceEqual(b));
                                    Aoi2Message.TryRemove(b.Key, out _);
                                }
                                //});

                                //检测完成
                                //LockMethod1(() =>
                                //{
                                var c = Aoi2Message.Where(item =>
                              item.Key[2] == 2 && item.Value.Skip(34).Take(44 - 34).ToArray().SequenceEqual(destination2)
                               ).FirstOrDefault();
                                //    var c = Aoi2Message.Find(item =>
                                //    item[2] == 2 && item.Skip(34).Take(44 - 34).ToArray().SequenceEqual(destination2)
                                //);
                                if (!c.Equals(default(KeyValuePair<byte[], byte[]>)))
                                {
                                    var re = new byte[value.BytesOriginal.Length];
                                    Array.Copy(value.BytesOriginal, re, value.BytesOriginal.Length);
                                    re[1] = 1;
                                    re[2] = 2;
                                    re[3] = 0;
                                    if (c.Value[11] == 2)
                                    {
                                        re[13] = 2;
                                    }
                                    else
                                    {
                                        re[13] = 1;
                                    }
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

                                    //re[12] = c[11];
                                    _remoteUdp?.SendAsync(_plcIpEndPoint, re2.Concat(waferData).ToArray());
                                    Logger?.Info($"{label19.Text}-{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}发送结果O->PLC:");
                                    Logger?.Info(re);
                                    Logger?.Info("----------------------------------------------------");
                                    complete = false;
                                    //Aoi2Message.RemoveAll(item => item.SequenceEqual(c));
                                    Aoi2Message.TryRemove(c.Key, out _);
                                }
                                //});

                                var afterDt = DateTime.Now;
                                var ts = afterDt.Subtract(beforeDt);
                                timeOut = ts.Ticks / 10000;
                            }
                            //RemoveQueue.Enqueue(value);
                            break;

                        case 3:
                            var destination3 = value.Bytes1.Skip(34).Take(44 - 34).ToArray();
                            var destination4 = value.Bytes2.Skip(34).Take(44 - 34).ToArray();
                            timeOut = 0;
                            beforeDt = DateTime.Now;
                            while ((inPhoto || photoCompleted || complete) && timeOut < _timeout)
                            {
                                byte[] a = default;
                                byte[] a1 = default;
                                KeyValuePair<byte[], byte[]> b = default;
                                KeyValuePair<byte[], byte[]> b1 = default;
                                KeyValuePair<byte[], byte[]> c = default;
                                KeyValuePair<byte[], byte[]> c1 = default;

                                //拍照中
                                //LockMethod(() =>
                                //{
                                //    a = Aoi1Message.Find(item =>
                                //        item[2] == 0 && item.Skip(34).Take(44 - 34).ToArray()
                                //            .SequenceEqual(destination3)
                                //    );
                                //});
                                //LockMethod1(() =>
                                //{
                                //    a1 = Aoi2Message.Find(item =>
                                //        item[2] == 0 && item.Skip(34).Take(44 - 34).ToArray()
                                //            .SequenceEqual(destination4)
                                //    );
                                //});

                                //if (a != null && a1 != null)
                                //{
                                //    var re = new byte[value.Length];
                                //    Array.Copy(value, re, value.Length);
                                //    re[1] = 1;
                                //    re[2] = 0;
                                //    re[3] = 0;
                                //    _remoteUdp?.SendAsync(_plcIpEndPoint, re);
                                //    Logger?.Info($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}拍照中O->PLC");
                                //    Logger?.Info(re);
                                //    Logger?.Info("----------------------------------------------------");
                                //    inPhoto = false;
                                //    LockMethod(() => { Aoi1Message.RemoveAll(item => item.SequenceEqual(a)); });
                                //    LockMethod1(() => { Aoi2Message.RemoveAll(item => item.SequenceEqual(a1)); });
                                //}

                                //拍照完成
                                //LockMethod(() =>
                                //{
                                b = Aoi1Message.Where(item =>
                             item.Key[2] == 1 && item.Value.Skip(34).Take(44 - 34).ToArray().SequenceEqual(destination3)
                             ).FirstOrDefault();
                                //     b = Aoi1Message.Find(item =>
                                //    item[2] == 1 && item.Skip(34).Take(44 - 34).ToArray().SequenceEqual(destination3)
                                //);
                                //});
                                //LockMethod1(() =>
                                //{
                                b1 = Aoi2Message.Where(item =>
                               item.Key[2] == 1 && item.Value.Skip(34).Take(44 - 34).ToArray().SequenceEqual(destination4)
                             ).FirstOrDefault();
                                //    b1 = Aoi2Message.Find(item =>
                                //    item[2] == 1 && item.Skip(34).Take(44 - 34).ToArray().SequenceEqual(destination4)
                                //);
                                //});

                                if (!b.Equals(default(KeyValuePair<byte[], byte[]>)) && !b1.Equals(default(KeyValuePair<byte[], byte[]>)))
                                {
                                    var re = new byte[value.BytesOriginal.Length];
                                    Array.Copy(value.BytesOriginal, re, value.BytesOriginal.Length);
                                    re[1] = 1;
                                    re[2] = 1;
                                    re[3] = 0;
                                    _remoteUdp?.SendAsync(_plcIpEndPoint, re);
                                    Logger?.Info($"{label19.Text}-{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}拍照完成O->PLC:");
                                    Logger?.Info(re);
                                    Logger?.Info("----------------------------------------------------");
                                    photoCompleted = false;

                                    //LockMethod(() =>
                                    //{
                                    //Aoi1Message.RemoveAll(item => item.SequenceEqual(b));
                                    Aoi1Message.TryRemove(b.Key, out _);
                                    //});
                                    //LockMethod1(() =>
                                    //{
                                    //Aoi2Message.RemoveAll(item => item.SequenceEqual(b1));
                                    Aoi2Message.TryRemove(b1.Key, out _);
                                    //});
                                }

                                //检测完成
                                //LockMethod(() =>
                                //{
                                c = Aoi1Message.Where(item =>
                                item.Key[2] == 2 && item.Value.Skip(34).Take(44 - 34).ToArray().SequenceEqual(destination3)
                            ).FirstOrDefault();
                                //    c = Aoi1Message.Find(item =>
                                //    item[2] == 2 && item.Skip(34).Take(44 - 34).ToArray().SequenceEqual(destination3)
                                //);
                                //});
                                //LockMethod1(() =>
                                //{
                                c1 = Aoi2Message.Where(item =>
                               item.Key[2] == 2 && item.Value.Skip(34).Take(44 - 34).ToArray().SequenceEqual(destination4)
                             ).FirstOrDefault();
                                //    c1 = Aoi2Message.Find(item =>
                                //    item[2] == 2 && item.Skip(34).Take(44 - 34).ToArray().SequenceEqual(destination4)
                                //);
                                //});

                                if (!c.Equals(default(KeyValuePair<byte[], byte[]>)) && !c1.Equals(default(KeyValuePair<byte[], byte[]>)))
                                {
                                    var re = new byte[value.BytesOriginal.Length];
                                    Array.Copy(value.BytesOriginal, re, value.BytesOriginal.Length);
                                    re[1] = 1;
                                    re[2] = 2;
                                    re[3] = 0;
                                    if (c.Value[11] == 2 || c1.Value[11] == 2)
                                    {
                                        re[13] = 2;
                                    }
                                    else
                                    {
                                        re[13] = 1;
                                    }
                                    //re[10] = c[10];
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
                                    //LockMethod(() => {
                                    //Aoi1Message.RemoveAll(item => item.SequenceEqual(c));
                                    Aoi1Message.TryRemove(c.Key, out _);
                                    //});
                                    //LockMethod1(() =>
                                    //{
                                    //Aoi2Message.RemoveAll(item => item.SequenceEqual(c1));
                                    Aoi2Message.TryRemove(c1.Key, out _);
                                    //});
                                }
                                var afterDt = DateTime.Now;
                                var ts = afterDt.Subtract(beforeDt);
                                timeOut = ts.Ticks / 10000;
                            }
                            //RemoveQueue.Enqueue(value);
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

        #region 清除信号

        /// <summary>
        /// 转发AOI发送的检测清除信号
        /// </summary>
        //private void Remove()
        //{
        //    while (true)
        //    {
        //        _tokenSource2?.Token.ThrowIfCancellationRequested();
        //        try
        //        {
        //            long timeOut;
        //            DateTime beforeDt = default;
        //            byte[] value = default;
        //            var clear = true;
        //            var complete = true;
        //            if (RemoveQueue == null || _plcIpEndPoint == null)
        //            {
        //                continue;
        //            }
        //            RemoveQueue.Dequeue(out value);
        //            switch (value[66])
        //            {
        //                case 1:
        //                    var destination1 = value.Skip(34).Take(44 - 34).ToArray();
        //                    timeOut = 0;
        //                    beforeDt = DateTime.Now;
        //                    while ((clear || complete) && timeOut < 200)
        //                    {
        //                        //发送检测清空信号
        //                        LockMethod(() =>
        //                        {
        //                            var a = Aoi1Message.Find(item =>
        //                                item[1] == 0 && item[2] == 3 && item.Skip(34).Take(44 - 34).ToArray()
        //                                    .SequenceEqual(destination1)
        //                            );
        //                            if (a != null)
        //                            {
        //                                var re = new byte[value.Length];
        //                                Array.Copy(value, re, value.Length);
        //                                re[1] = 0;
        //                                re[2] = 3;
        //                                re[3] = 1;
        //                                _remoteUdp?.SendAsync(_plcIpEndPoint, re);
        //                                Logger?.Info(re);
        //                                clear = false;
        //                                Aoi1Message.RemoveAll(item => item.SequenceEqual(a));
        //                            }
        //                        });
        //                        //发送就绪信号
        //                        LockMethod(() =>
        //                        {
        //                            var b = Aoi1Message.Find(item =>
        //                                    item[1] == 1 && item[2] == 3 && item.Skip(34).Take(44 - 34).ToArray().SequenceEqual(destination1)
        //                               );
        //                            if (b != null)
        //                            {
        //                                byte[] re = new byte[value.Length];
        //                                Array.Copy(value, re, value.Length);
        //                                re[1] = 1;
        //                                re[2] = 3;
        //                                re[3] = 1;
        //                                _remoteUdp?.SendAsync(_plcIpEndPoint, re);
        //                                Logger?.Info(re);
        //                                complete = false;
        //                                Aoi1Message.RemoveAll(item => item.SequenceEqual(b));
        //                            }
        //                        });

        //                        var afterDt = DateTime.Now;
        //                        var ts = afterDt.Subtract(beforeDt);
        //                        timeOut = ts.Ticks / 10000;
        //                    }
        //                    break;

        //                case 2:
        //                    var destination2 = value.Skip(44).Take(54 - 44).ToArray();
        //                    timeOut = 0;
        //                    beforeDt = DateTime.Now;
        //                    while ((clear || complete) && timeOut < 200)
        //                    {
        //                        //发送检测清空信号
        //                        LockMethod1(() =>
        //                        {
        //                            var a = Aoi2Message.Find(item =>
        //                                item[1] == 0 && item[2] == 3 && item.Skip(34).Take(44 - 34).ToArray()
        //                                    .SequenceEqual(destination2)
        //                            );
        //                            if (a != null)
        //                            {
        //                                var re = new byte[value.Length];
        //                                Array.Copy(value, re, value.Length);
        //                                re[1] = 0;
        //                                re[2] = 3;
        //                                re[3] = 1;
        //                                _remoteUdp?.SendAsync(_plcIpEndPoint, re);
        //                                Logger?.Info(re);
        //                                clear = false;
        //                                Aoi2Message.RemoveAll(item => item.SequenceEqual(a));
        //                            }
        //                        });

        //                        //发送就绪信号
        //                        LockMethod1(() =>
        //                        {
        //                            var b = Aoi2Message.Find(item =>
        //                            item[1] == 1 && item[2] == 3 && item.Skip(34).Take(44 - 34).ToArray().SequenceEqual(destination2)
        //                        );
        //                            if (b != null)
        //                            {
        //                                byte[] re = new byte[value.Length];
        //                                Array.Copy(value, re, value.Length);
        //                                re[1] = 1;
        //                                re[2] = 3;
        //                                re[3] = 1;
        //                                _remoteUdp?.SendAsync(_plcIpEndPoint, re);
        //                                Logger?.Info(re);
        //                                complete = false;
        //                                Aoi2Message.RemoveAll(item => item.SequenceEqual(b));
        //                            }
        //                        });

        //                        var afterDt = DateTime.Now;
        //                        var ts = afterDt.Subtract(beforeDt);
        //                        timeOut = ts.Ticks / 10000;
        //                    }

        //                    break;

        //                case 3:
        //                    var destination3 = value.Skip(34).Take(44 - 34).ToArray();
        //                    var destination4 = value.Skip(44).Take(54 - 44).ToArray();
        //                    timeOut = 0;
        //                    beforeDt = DateTime.Now;
        //                    while ((clear || complete) && timeOut < 200)
        //                    {
        //                        byte[] a = default;
        //                        byte[] a1 = default;
        //                        byte[] b = default;
        //                        byte[] b1 = default;

        //                        //发送检测清空信号
        //                        LockMethod(() =>
        //                        {
        //                            a = Aoi1Message.Find(item =>
        //                               item[1] == 0 && item[2] == 3 && item.Skip(34).Take(44 - 34).ToArray()
        //                                   .SequenceEqual(destination3)
        //                           );
        //                        });
        //                        LockMethod1(() =>
        //                        {
        //                            a1 = Aoi2Message.Find(item =>
        //                               item[1] == 0 && item[2] == 3 && item.Skip(34).Take(44 - 34).ToArray()
        //                                   .SequenceEqual(destination4)
        //                           );
        //                        });

        //                        if (a != null && a1 != null)
        //                        {
        //                            var re = new byte[value.Length];
        //                            Array.Copy(value, re, value.Length);
        //                            re[1] = 0;
        //                            re[2] = 3;
        //                            re[3] = 1;
        //                            _remoteUdp?.SendAsync(_plcIpEndPoint, re);
        //                            Logger?.Info(re);
        //                            clear = false;
        //                            LockMethod(() => { Aoi1Message.RemoveAll(item => item.SequenceEqual(a)); });
        //                            LockMethod1(() => { Aoi2Message.RemoveAll(item => item.SequenceEqual(a1)); });
        //                        }

        //                        //发送就绪信号
        //                        LockMethod(() =>
        //                        {
        //                            b = Aoi1Message.Find(item =>
        //                           item[1] == 1 && item[2] == 3 && item.Skip(34).Take(44 - 34).ToArray().SequenceEqual(destination3)
        //                       );
        //                        });
        //                        LockMethod1(() =>
        //                        {
        //                            b1 = Aoi2Message.Find(item =>
        //                            item[1] == 1 && item[2] == 3 && item.Skip(34).Take(44 - 34).ToArray().SequenceEqual(destination4)
        //                        );
        //                        });

        //                        if (b != null && b1 != null)
        //                        {
        //                            var re = new byte[value.Length];
        //                            Array.Copy(value, re, value.Length);
        //                            re[1] = 1;
        //                            re[2] = 3;
        //                            re[3] = 1;
        //                            _remoteUdp?.SendAsync(_plcIpEndPoint, re);
        //                            Logger?.Info(re);
        //                            complete = false;
        //                            LockMethod(() => { Aoi1Message.RemoveAll(item => item.SequenceEqual(b)); });
        //                            LockMethod1(() => { Aoi2Message.RemoveAll(item => item.SequenceEqual(b1)); });
        //                        }

        //                        var afterDt = DateTime.Now;
        //                        var ts = afterDt.Subtract(beforeDt);
        //                        timeOut = ts.Ticks / 10000;
        //                    }

        //                    break;

        //                default:
        //                    break;
        //            }
        //        }
        //        catch (Exception e)
        //        {
        //            Logger?.Error(e.Message, e);
        //        }
        //    }
        //}

        #endregion 清除信号

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
        /// <param name="action"></param>
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
        /// <param name="action"></param>
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
        /// <param name="action"></param>
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
        /// <param name="action"></param>
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
                    // 读取 配置文件
                    var jsonString = File.ReadAllText(path);
                    // 将 读取到的内容 反序列化 为 JSON DOM 对象
                    var jsonNode = JsonNode.Parse(jsonString)!;
                    // 从 DOM 对象中取值并 赋给 控件
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
                PlcIp.Enabled = true;
                PlcPort.Enabled = true;
                Plc_oneIp.Enabled = true;
                Plc_onePort.Enabled = true;

                Aoi1_oneIp.Enabled = true;
                Aoi_onePort.Enabled = true;
                Aoi1Ip.Enabled = true;
                Aoi1Port.Enabled = true;

                Aoi2_oneIp.Enabled = true;
                Aoi2_onePort.Enabled = true;
                Aoi2Ip.Enabled = true;
                Aoi2Port.Enabled = true;
                numericUpDown1.Enabled = true;
                numericUpDown2.Enabled = true;
            }
            else
            {
                PlcIp.Enabled = false;
                PlcPort.Enabled = false;
                Plc_oneIp.Enabled = false;
                Plc_onePort.Enabled = false;

                Aoi1_oneIp.Enabled = false;
                Aoi_onePort.Enabled = false;
                Aoi1Ip.Enabled = false;
                Aoi1Port.Enabled = false;

                Aoi2_oneIp.Enabled = false;
                Aoi2_onePort.Enabled = false;
                Aoi2Ip.Enabled = false;
                Aoi2Port.Enabled = false;
                numericUpDown1.Enabled = false;
                numericUpDown2.Enabled = false;
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
                    // 创建 DirectoryInfo 对象
                    DirectoryInfo directoryInfo = new(item);
                    // 获取目录创建时间
                    DateTime creationTime = directoryInfo.CreationTime;
                    if (DateTime.Now.AddDays(-7) > creationTime)
                    {
                        // 使用 Directory.Delete 删除目录及其内容
                        // 第二个参数 'true' 指定递归删除（包括子目录和文件）
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
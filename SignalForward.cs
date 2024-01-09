using SignalForward.UDP;
using System.Net;
using System.Reflection;

namespace SignalForward
{
    public partial class SignalForward : Form
    {
        public log4net.ILog? log;

        /// <summary>
        /// 远程Udp对象
        /// </summary>
        private xxUDPSyncServer? _remoteUdp;

        /// <summary>
        /// PLC
        /// </summary>
        private IPEndPoint _plcIpEndPoint;

        /// <summary>
        /// AOI1
        /// </summary>
        private IPEndPoint _Aoi1PortEndPoint;

        /// <summary>
        /// AOI2
        /// </summary>
        private IPEndPoint _Aoi2PortEndPoint;

        /// <summary>
        /// 本地连接
        /// </summary>
        private xxUDPSyncServer? _localUdp;

        /// <summary>
        /// 本地连接1
        /// </summary>
        private xxUDPSyncServer? _localUdp1;

        /// <summary>
        /// 通讯信号
        /// </summary>
        public WHCurrentQueue<byte[]>? RemoteQueue;

        /// <summary>
        /// 待删除的信号
        /// </summary>
        public WHCurrentQueue<byte[]>? RemoveQueue;

        public List<byte[]> Aoi1Message = new List<byte[]>();
        public List<byte[]> Aoi2Message = new List<byte[]>();

        public SignalForward()
        {
            log = log4net.LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
            RemoteQueue = new WHCurrentQueue<byte[]>("等待发送结果", log);
            RemoveQueue = new WHCurrentQueue<byte[]>("等待删除结果", log);
            InitializeComponent();
            PlcIp.DataBindings.Add("Enabled", RemoteBnt, "Enabled");
            PlcPort.DataBindings.Add("Enabled", RemoteBnt, "Enabled");
            Plc_oneIp.DataBindings.Add("Enabled", RemoteBnt, "Enabled");
            Plc_onePort.DataBindings.Add("Enabled", RemoteBnt, "Enabled");

            Aoi1_oneIp.DataBindings.Add("Enabled", button1, "Enabled");
            Aoi_onePort.DataBindings.Add("Enabled", button1, "Enabled");
            Aoi1Ip.DataBindings.Add("Enabled", button1, "Enabled");
            Aoi1Port.DataBindings.Add("Enabled", button1, "Enabled");

            Aoi2_oneIp.DataBindings.Add("Enabled", button2, "Enabled");
            Aoi2_onePort.DataBindings.Add("Enabled", button2, "Enabled");
            Aoi2Ip.DataBindings.Add("Enabled", button2, "Enabled");
            Aoi2Port.DataBindings.Add("Enabled", button2, "Enabled");

            Task.Factory.StartNew(Transmit, TaskCreationOptions.LongRunning);
            Task.Factory.StartNew(Remove, TaskCreationOptions.LongRunning);
        }

        public void Communication()
        {
            byte[] data;

            while (true)
            {
                if (_localUdp == null || _localUdp1 == null)
                {
                    continue;
                }
                RemoteQueue.Dequeue(out data);
                switch (data[43])
                {
                    case 1:
                        if (data[3] == 1)
                        {
                            _localUdp.Send(_Aoi1PortEndPoint, data);
                            bool control = true;
                            while (control)
                            {
                                IPEndPoint rEndPoint = default;
                                byte[] buff = _localUdp._server.Receive(ref rEndPoint);
                                if (buff != default)
                                {
                                    _remoteUdp.SendAsync(_plcIpEndPoint, buff);
                                }
                                if (buff[2] == 2)
                                {
                                    break;
                                }
                            }
                        }
                        break;

                    case 2:
                        if (data[3] == 1)
                        {
                            _localUdp1.Send(_Aoi2PortEndPoint, data);
                            bool control = true;
                            while (control)
                            {
                                IPEndPoint rEndPoint = default;
                                byte[] buff = _localUdp1._server.Receive(ref rEndPoint);
                                if (buff != default)
                                {
                                    _remoteUdp.SendAsync(_plcIpEndPoint, buff);
                                }
                                if (buff[2] == 2)
                                {
                                    break;
                                }
                            }
                        }
                        break;

                    case 3:
                        if (data[3] == 1)
                        {
                            //事务列表
                            List<Task<byte[]>> tasks = new List<Task<byte[]>>();
                            _localUdp.SendAsync(_Aoi1PortEndPoint, data);
                            _localUdp1.SendAsync(_Aoi2PortEndPoint, data);
                            bool control = true;
                            while (control)
                            {
                                Task<byte[]> result = new Task<byte[]>((() =>
                                {
                                    IPEndPoint ipEnd = default;
                                    return _localUdp._server.Receive(ref ipEnd);
                                }));
                                tasks.Add(result);
                                Task<byte[]> result1 = new Task<byte[]>((() =>
                                {
                                    IPEndPoint ipEnd = default;
                                    return _localUdp1._server.Receive(ref ipEnd);
                                }));
                                tasks.Add(result1);
                                result.Start();
                                result1.Start();

                                List<byte[]> r = new List<byte[]>();
                                foreach (var item in tasks)
                                {
                                    r.Add(item.Result);
                                }

                                if (r[0][2] == 2 && r[1][2] == 2)
                                {
                                    break;
                                }
                            }
                        }
                        break;

                    default:
                        break;
                }
            }
        }

        private void RemoteBnt_Click_1(object sender, EventArgs e)
        {
            try
            {
                RemoteBnt.Enabled = false;
                button3.Enabled = true;
                _plcIpEndPoint = new IPEndPoint(IPAddress.Parse(PlcIp.Text.Trim()), int.Parse(PlcPort.Text.Trim()));

                _remoteUdp = new xxUDPSyncServer(IPAddress.Parse(Plc_oneIp.Text.Trim()), int.Parse(Plc_onePort.Text.Trim()), log);
                _remoteUdp.DataReceived += (object? sender, byte[] dataBytes) =>
                {
                    //byte[] destination = dataBytes.Skip(34).Take(44 - 34).ToArray();

                    //var b = destination.SequenceEqual(a);
                    if (_localUdp != null || _localUdp1 != null)
                    {
                        switch (dataBytes[66])
                        {
                            case 1:
                                if (dataBytes[3] == 1)
                                {
                                    byte[] newBytes = new byte[dataBytes.Length];
                                    newBytes[3] = 1;
                                    var data = dataBytes.Skip(34).Take(44 - 34).ToArray();
                                    for (int i = 0; i < data.Length; i++)
                                    {
                                        newBytes[34 + i] = data[i];
                                    }
                                    _localUdp.Send(_Aoi1PortEndPoint, newBytes);
                                    RemoteQueue.Enqueue(dataBytes);
                                }
                                break;

                            case 2:
                                if (dataBytes[3] == 1)
                                {
                                    byte[] newBytes = new byte[dataBytes.Length];
                                    newBytes[3] = 1;
                                    var data = dataBytes.Skip(44).Take(54 - 44).ToArray();
                                    for (int i = 0; i < data.Length; i++)
                                    {
                                        newBytes[34 + i] = data[i];
                                    }
                                    _localUdp1.Send(_Aoi2PortEndPoint, newBytes);
                                    RemoteQueue.Enqueue(dataBytes);
                                }
                                break;

                            case 3:
                                if (dataBytes[3] == 1)
                                {
                                    byte[] newBytes = new byte[dataBytes.Length];
                                    newBytes[3] = 1;
                                    var data = dataBytes.Skip(34).Take(44 - 34).ToArray();
                                    for (int i = 0; i < data.Length; i++)
                                    {
                                        newBytes[34 + i] = data[i];
                                    }
                                    byte[] newBytes1 = new byte[128];
                                    newBytes1[3] = 1;
                                    var data1 = dataBytes.Skip(44).Take(54 - 44).ToArray();
                                    for (int i = 0; i < data1.Length; i++)
                                    {
                                        newBytes1[34 + i] = data1[i];
                                    }
                                    _localUdp.Send(_Aoi1PortEndPoint, newBytes);
                                    _localUdp1.Send(_Aoi2PortEndPoint, newBytes1);
                                    RemoteQueue.Enqueue(dataBytes);
                                }
                                break;

                            default:
                                break;
                        }

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

            }

        }

        private void button1_Click(object sender, EventArgs e)
        {
            try
            {
                button1.Enabled = false;
                button4.Enabled = true;
                _Aoi1PortEndPoint =
                    new IPEndPoint(IPAddress.Parse(Aoi1Ip.Text.Trim()), int.Parse(Aoi1Port.Text.Trim()));
                _localUdp = new xxUDPSyncServer(IPAddress.Parse(Aoi1_oneIp.Text.Trim()),
                    int.Parse(Aoi_onePort.Text.Trim()), log);
                _localUdp.DataReceived += (o, bytes) =>
                {
                    log.Info("AOI1->O:" + bytes);
                    lock (this)
                    {
                        Aoi1Message.Add(bytes);

                    }
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
            }
        }

        private void button2_Click_1(object sender, EventArgs e)
        {
            try
            {
                button2.Enabled = false;
                button5.Enabled = true;
                _Aoi2PortEndPoint =
                    new IPEndPoint(IPAddress.Parse(Aoi2Ip.Text.Trim()), int.Parse(Aoi2Port.Text.Trim()));
                _localUdp1 = new xxUDPSyncServer(IPAddress.Parse(Aoi2_oneIp.Text.Trim()),
                    int.Parse(Aoi2_onePort.Text.Trim()), log);
                _localUdp1.DataReceived += (o, bytes) =>
                {
                    log.Info("AOI2->O:" + bytes);
                    lock (this)
                    {
                        Aoi2Message.Add(bytes);
                    }
                };
                _localUdp1.Start();
            }
            catch (Exception exception)
            {
                button2.Enabled = true;
                button5.Enabled = false;
                if (_localUdp1 != null)
                {
                    _localUdp.Stop();
                    _localUdp.Dispose();
                    _localUdp = null;
                }
            }
        }


        private void Transmit()
        {
            while (true)
            {
                try
                {
                    long timeOut = 0;
                    DateTime beforeDT = default;
                    //拍照中
                    bool inPhoto = true;
                    //拍照完成
                    bool photoCompleted = true;
                    //检测完成
                    bool complete = true;
                    //收到的消息
                    byte[] value = default;
                    RemoteQueue.Dequeue(out value);
                    log.Info(value);
                    switch (value[66])
                    {
                        case 1:
                            byte[] destination1 = value.Skip(34).Take(44 - 34).ToArray();
                            timeOut = 0;
                            beforeDT = System.DateTime.Now;
                            while ((inPhoto || photoCompleted || complete) && timeOut < 1000)
                            {
                                lock (this)
                                {
                                    //拍照中
                                    var a = Aoi1Message.Find(item =>
                                           item[2] == 0 && item.Skip(34).Take(44 - 34).ToArray().SequenceEqual(destination1)
                                       );
                                    if (a != null)
                                    {
                                        byte[] re = new byte[value.Length];
                                        Array.Copy(value, re, value.Length);
                                        re[1] = 1;
                                        re[2] = 0;
                                        re[3] = 0;
                                        _remoteUdp.SendAsync(_plcIpEndPoint, re);
                                        log.Info("O->PLC" + re);
                                        inPhoto = false;
                                        Aoi1Message.RemoveAll(item => item.SequenceEqual(a));
                                    }

                                    //拍照完成
                                    var b = Aoi1Message.Find(item =>
                                           item[2] == 1 && item.Skip(34).Take(44 - 34).ToArray().SequenceEqual(destination1)
                                       );
                                    if (b != null)
                                    {
                                        byte[] re = new byte[value.Length];
                                        Array.Copy(value, re, value.Length);
                                        re[1] = 1;
                                        re[2] = 1;
                                        re[3] = 0;
                                        _remoteUdp.SendAsync(_plcIpEndPoint, re);
                                        log.Info("O->PLC" + re);
                                        photoCompleted = false;
                                        Aoi1Message.RemoveAll(item => item.SequenceEqual(b));
                                    }

                                    //检测完成
                                    var c = Aoi1Message.Find(item =>
                                          item[2] == 2 && item.Skip(34).Take(44 - 34).ToArray().SequenceEqual(destination1)
                                      );
                                    if (c != null)
                                    {
                                        byte[] re = new byte[value.Length];
                                        Array.Copy(value, re, value.Length);
                                        re[1] = 1;
                                        re[2] = 2;
                                        re[3] = 0;
                                        re[9] = c[9];
                                        re[10] = c[10];
                                        _remoteUdp.SendAsync(_plcIpEndPoint, re);
                                        log.Info("O->PLC" + re);
                                        complete = false;
                                        Aoi1Message.RemoveAll(item => item.SequenceEqual(c));
                                    }
                                    DateTime afterDT = System.DateTime.Now;
                                    TimeSpan ts = afterDT.Subtract(beforeDT);
                                    timeOut = ts.Milliseconds;
                                }
                            }
                            RemoveQueue.Enqueue(value);
                            break;

                        case 2:
                            byte[] destination2 = value.Skip(44).Take(54 - 44).ToArray();
                            timeOut = 0;
                            beforeDT = System.DateTime.Now;
                            while ((inPhoto || photoCompleted || complete) && timeOut < 1000)
                            {
                                lock (this)
                                {
                                    //拍照中
                                    var a = Aoi2Message.Find(item =>
                                        item[2] == 0 && item.Skip(34).Take(44 - 34).ToArray().SequenceEqual(destination2)
                                    );
                                    if (a != null)
                                    {
                                        byte[] re = new byte[value.Length];
                                        Array.Copy(value, re, value.Length);
                                        re[1] = 1;
                                        re[2] = 0;
                                        re[3] = 0;
                                        _remoteUdp.SendAsync(_plcIpEndPoint, re);
                                        log.Info("O->PLC" + re);
                                        inPhoto = false;
                                        Aoi2Message.RemoveAll(item => item.SequenceEqual(a));
                                    }

                                    //拍照完成
                                    var b = Aoi2Message.Find(item =>
                                        item[2] == 1 && item.Skip(34).Take(44 - 34).ToArray().SequenceEqual(destination2)
                                    );
                                    if (b != null)
                                    {
                                        byte[] re = new byte[value.Length];
                                        Array.Copy(value, re, value.Length);
                                        re[1] = 1;
                                        re[2] = 1;
                                        re[3] = 0;
                                        _remoteUdp.SendAsync(_plcIpEndPoint, re);
                                        log.Info("O->PLC" + re);
                                        photoCompleted = false;
                                        Aoi2Message.RemoveAll(item => item.SequenceEqual(b));
                                    }

                                    //检测完成
                                    var c = Aoi2Message.Find(item =>
                                        item[2] == 2 && item.Skip(34).Take(44 - 34).ToArray().SequenceEqual(destination2)
                                    );
                                    if (c != null)
                                    {
                                        byte[] re = new byte[value.Length];
                                        Array.Copy(value, re, value.Length);
                                        re[1] = 1;
                                        re[2] = 2;
                                        re[3] = 0;
                                        re[9] = c[9];
                                        re[10] = c[10];
                                        _remoteUdp.SendAsync(_plcIpEndPoint, re);
                                        log.Info(re);
                                        complete = false;
                                        Aoi2Message.RemoveAll(item => item.SequenceEqual(c));
                                    }
                                    DateTime afterDT = System.DateTime.Now;
                                    TimeSpan ts = afterDT.Subtract(beforeDT);
                                    timeOut = ts.Milliseconds;
                                }
                            }
                            RemoveQueue.Enqueue(value);
                            break;

                        case 3:
                            byte[] destination3 = value.Skip(34).Take(44 - 34).ToArray();
                            byte[] destination4 = value.Skip(44).Take(54 - 44).ToArray();
                            timeOut = 0;
                            beforeDT = System.DateTime.Now;
                            while ((inPhoto || photoCompleted || complete) && timeOut < 1000)
                            {
                                lock (this)
                                {
                                    //拍照中
                                    var a = Aoi1Message.Find(item =>
                                        item[2] == 0 && item.Skip(34).Take(44 - 34).ToArray().SequenceEqual(destination3)
                                    );
                                    var a1 = Aoi2Message.Find(item =>
                                        item[2] == 0 && item.Skip(34).Take(44 - 34).ToArray().SequenceEqual(destination4)
                                    );
                                    if (a != null || a1 != null)
                                    {
                                        if (a != null)
                                        {
                                            byte[] re = new byte[value.Length];
                                            Array.Copy(value, re, value.Length);
                                            re[1] = 1;
                                            re[2] = 0;
                                            re[3] = 0;
                                            _remoteUdp.SendAsync(_plcIpEndPoint, re);
                                            log.Info("O->PLC" + re);
                                            inPhoto = false;
                                            Aoi1Message.RemoveAll(item => item.SequenceEqual(a));
                                            //Aoi2Message.RemoveAll(item => item.SequenceEqual(a1));
                                        }
                                        else if (a1 != null)
                                        {
                                            byte[] re = new byte[value.Length];
                                            Array.Copy(value, re, value.Length);
                                            re[1] = 1;
                                            re[2] = 0;
                                            re[3] = 0;
                                            _remoteUdp.SendAsync(_plcIpEndPoint, re);
                                            log.Info("O->PLC" + re);
                                            inPhoto = false;
                                            //Aoi1Message.RemoveAll(item => item.SequenceEqual(a));
                                            Aoi2Message.RemoveAll(item => item.SequenceEqual(a1));
                                        }

                                    }

                                    //拍照完成
                                    var b = Aoi1Message.Find(item =>
                                        item[2] == 1 && item.Skip(34).Take(44 - 34).ToArray().SequenceEqual(destination3)
                                    );
                                    var b1 = Aoi2Message.Find(item =>
                                        item[2] == 1 && item.Skip(34).Take(44 - 34).ToArray().SequenceEqual(destination4)
                                    );
                                    if (b != null && b1 != null)
                                    {
                                        byte[] re = new byte[value.Length];
                                        Array.Copy(value, re, value.Length);
                                        re[1] = 1;
                                        re[2] = 1;
                                        re[3] = 0;
                                        _remoteUdp.SendAsync(_plcIpEndPoint, re);
                                        log.Info(re);
                                        photoCompleted = false;
                                        Aoi1Message.RemoveAll(item => item.SequenceEqual(b));
                                        Aoi2Message.RemoveAll(item => item.SequenceEqual(b1));
                                    }

                                    //检测完成
                                    var c = Aoi1Message.Find(item =>
                                        item[2] == 2 && item.Skip(34).Take(44 - 34).ToArray().SequenceEqual(destination3)
                                    );
                                    var c1 = Aoi2Message.Find(item =>
                                        item[2] == 2 && item.Skip(34).Take(44 - 34).ToArray().SequenceEqual(destination4)
                                    );
                                    if (c != null && c1 != null)
                                    {
                                        byte[] re = new byte[value.Length];
                                        Array.Copy(value, re, value.Length);
                                        re[1] = 1;
                                        re[2] = 2;
                                        re[3] = 0;
                                        re[9] = c[9];
                                        re[10] = c[10];
                                        re[11] = c1[9];
                                        re[12] = c1[10];
                                        _remoteUdp.SendAsync(_plcIpEndPoint, re);
                                        log.Info("O->PLC" + re);
                                        complete = false;
                                        Aoi1Message.RemoveAll(item => item.SequenceEqual(c));
                                        Aoi2Message.RemoveAll(item => item.SequenceEqual(c1));
                                    }
                                    DateTime afterDT = System.DateTime.Now;
                                    TimeSpan ts = afterDT.Subtract(beforeDT);
                                    timeOut = ts.Milliseconds;
                                }
                            }
                            RemoveQueue.Enqueue(value);
                            break;

                        default:
                            break;
                    }
                }
                catch (Exception e)
                {
                    log.Debug(e.Message, e);
                }

            }
        }

        private void Remove()
        {
            while (true)
            {
                try
                {
                    long timeOut = 0;
                    DateTime beforeDT = default;
                    byte[] value = default;
                    bool clear = true;
                    bool complete = true;
                    RemoveQueue.Dequeue(out value);
                    switch (value[66])
                    {
                        case 1:
                            byte[] destination1 = value.Skip(34).Take(44 - 34).ToArray();
                            timeOut = 0;
                            beforeDT = System.DateTime.Now;
                            while ((clear || complete) && timeOut < 1000)
                            {
                                lock (this)
                                {
                                    //发送检测清空信号
                                    var a = Aoi1Message.Find(item =>
                                         item[1] == 0 && item[2] == 3 && item.Skip(34).Take(44 - 34).ToArray().SequenceEqual(destination1)
                                       );
                                    if (a != null)
                                    {
                                        byte[] re = new byte[value.Length];
                                        Array.Copy(value, re, value.Length);
                                        re[1] = 0;
                                        re[2] = 3;
                                        re[3] = 0;
                                        _remoteUdp.SendAsync(_plcIpEndPoint, re);
                                        log.Info(re);
                                        clear = false;
                                        Aoi1Message.RemoveAll(item => item.SequenceEqual(a));
                                    }

                                    //发送就绪信号
                                    var b = Aoi1Message.Find(item =>
                                            item[1] == 1 && item[2] == 3 && item.Skip(34).Take(44 - 34).ToArray().SequenceEqual(destination1)
                                       );
                                    if (b != null)
                                    {
                                        byte[] re = new byte[value.Length];
                                        Array.Copy(value, re, value.Length);
                                        re[1] = 1;
                                        re[2] = 3;
                                        re[3] = 0;
                                        _remoteUdp.SendAsync(_plcIpEndPoint, re);
                                        log.Info(re);
                                        complete = false;
                                        Aoi1Message.RemoveAll(item => item.SequenceEqual(b));
                                    }

                                    DateTime afterDT = System.DateTime.Now;
                                    TimeSpan ts = afterDT.Subtract(beforeDT);
                                    timeOut = ts.Milliseconds;
                                }
                            }

                            break;

                        case 2:
                            byte[] destination2 = value.Skip(44).Take(54 - 44).ToArray();
                            timeOut = 0;
                            beforeDT = System.DateTime.Now;
                            while ((clear || complete) && timeOut < 1000)
                            {
                                lock (this)
                                {
                                    //发送检测清空信号
                                    var a = Aoi2Message.Find(item =>
                                        item[1] == 0 && item[2] == 3 && item.Skip(34).Take(44 - 34).ToArray().SequenceEqual(destination2)
                                    );
                                    if (a != null)
                                    {
                                        byte[] re = new byte[value.Length];
                                        Array.Copy(value, re, value.Length);
                                        re[1] = 0;
                                        re[2] = 3;
                                        re[3] = 0;
                                        _remoteUdp.SendAsync(_plcIpEndPoint, re);
                                        log.Info(re);
                                        clear = false;
                                        Aoi2Message.RemoveAll(item => item.SequenceEqual(a));
                                    }

                                    //发送就绪信号
                                    var b = Aoi2Message.Find(item =>
                                        item[1] == 1 && item[2] == 3 && item.Skip(34).Take(44 - 34).ToArray().SequenceEqual(destination2)
                                    );
                                    if (b != null)
                                    {
                                        byte[] re = new byte[value.Length];
                                        Array.Copy(value, re, value.Length);
                                        re[1] = 1;
                                        re[2] = 3;
                                        re[3] = 0;
                                        _remoteUdp.SendAsync(_plcIpEndPoint, re);
                                        log.Info(re);
                                        complete = false;
                                        Aoi2Message.RemoveAll(item => item.SequenceEqual(b));
                                    }

                                    DateTime afterDT = System.DateTime.Now;
                                    TimeSpan ts = afterDT.Subtract(beforeDT);
                                    timeOut = ts.Milliseconds;
                                }
                            }

                            break;

                        case 3:
                            byte[] destination3 = value.Skip(34).Take(44 - 34).ToArray();
                            byte[] destination4 = value.Skip(44).Take(54 - 44).ToArray();
                            timeOut = 0;
                            beforeDT = System.DateTime.Now;
                            while ((clear || complete) && timeOut < 1000)
                            {
                                lock (this)
                                {
                                    //发送检测清空信号
                                    var a = Aoi1Message.Find(item =>
                                        item[1] == 0 && item[2] == 3 && item.Skip(34).Take(44 - 34).ToArray().SequenceEqual(destination3)
                                    );
                                    var a1 = Aoi2Message.Find(item =>
                                        item[1] == 0 && item[2] == 3 && item.Skip(34).Take(44 - 34).ToArray().SequenceEqual(destination4)
                                    );
                                    if (a != null && a1 != null)
                                    {
                                        byte[] re = new byte[value.Length];
                                        Array.Copy(value, re, value.Length);
                                        re[1] = 0;
                                        re[2] = 3;
                                        re[3] = 0;
                                        _remoteUdp.SendAsync(_plcIpEndPoint, re);
                                        log.Info(re);
                                        clear = false;
                                        Aoi1Message.RemoveAll(item => item.SequenceEqual(a));
                                        Aoi2Message.RemoveAll(item => item.SequenceEqual(a1));
                                    }

                                    //发送就绪信号
                                    var b = Aoi1Message.Find(item =>
                                        item[1] == 1 && item[2] == 3 && item.Skip(34).Take(44 - 34).ToArray().SequenceEqual(destination3)
                                    );
                                    var b1 = Aoi2Message.Find(item =>
                                        item[1] == 1 && item[2] == 3 && item.Skip(34).Take(44 - 34).ToArray().SequenceEqual(destination4)
                                    );
                                    if (b != null && b1 != null)
                                    {
                                        byte[] re = new byte[value.Length];
                                        Array.Copy(value, re, value.Length);
                                        re[1] = 1;
                                        re[2] = 3;
                                        re[3] = 0;
                                        _remoteUdp.SendAsync(_plcIpEndPoint, re);
                                        log.Info(re);
                                        complete = false;
                                        Aoi1Message.RemoveAll(item => item.SequenceEqual(b));
                                        Aoi2Message.RemoveAll(item => item.SequenceEqual(b1));

                                    }
                                    DateTime afterDT = System.DateTime.Now;
                                    TimeSpan ts = afterDT.Subtract(beforeDT);
                                    timeOut = ts.Milliseconds;
                                }
                            }

                            break;

                        default:
                            break;
                    }
                }
                catch (Exception e)
                {
                    log.Debug(e.Message, e);
                }

            }
        }

        private void button3_Click(object sender, EventArgs e)
        {
            try
            {
                button3.Enabled = false;
                RemoteBnt.Enabled = true;
                if (_remoteUdp != null)
                {
                    _remoteUdp.Stop();
                    _remoteUdp.Dispose();
                    _remoteUdp = null;
                }

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
                if (_localUdp != null)
                {
                    _localUdp.Stop();
                    _localUdp.Dispose();
                    _localUdp = null;
                }
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
                button5.Enabled = false;
                button2.Enabled = true;
                if (_localUdp1 != null)
                {
                    _localUdp1.Stop();
                    _localUdp1.Dispose();
                    _localUdp1 = null;
                }
            }
            catch (Exception exception)
            {
                Console.WriteLine(exception);
                throw;
            }
        }
    }
}
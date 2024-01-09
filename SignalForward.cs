using SignalForward.UDP;
using System.Net;
using System.Reflection;

namespace SignalForward
{
    public partial class SignalForward : Form
    {
        public log4net.ILog? Logger;

        /// <summary>
        /// 远程Udp对象
        /// </summary>
        private xxUDPSyncServer? _remoteUdp;

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

        /// <summary>
        /// AOI1发送的消息
        /// </summary>
        public List<byte[]> Aoi1Message = new();

        /// <summary>
        /// AOI2发送的消息
        /// </summary>
        public List<byte[]> Aoi2Message = new();

        public SignalForward()
        {
            Logger = log4net.LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
            RemoteQueue = new WHCurrentQueue<byte[]>("等待发送结果", Logger);
            RemoveQueue = new WHCurrentQueue<byte[]>("等待删除结果", Logger);
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

            Task.Factory.StartNew(CbTransmit, TaskCreationOptions.LongRunning);
            Task.Factory.StartNew(Remove, TaskCreationOptions.LongRunning);
        }

        #region 同步方式

        public void Communication()
        {
            while (true)
            {
                byte[] data;
                if (_localUdp == null || _localUdp1 == null || RemoteQueue == null)
                {
                    continue;
                }
                RemoteQueue.Dequeue(out data);
                switch (data[43])
                {
                    case 1:
                        if (data[3] == 1)
                        {
                            _localUdp.Send(_aoi1PortEndPoint, data);
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
                            _localUdp1.Send(_aoi2PortEndPoint, data);
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
                            _localUdp.SendAsync(_aoi1PortEndPoint, data);
                            _localUdp1.SendAsync(_aoi2PortEndPoint, data);
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

        #endregion 同步方式

        private void RemoteBnt_Click_1(object sender, EventArgs e)
        {
            try
            {
                if (Logger == null) return;

                RemoteBnt.Enabled = false;
                button3.Enabled = true;
                _plcIpEndPoint = new IPEndPoint(IPAddress.Parse(PlcIp.Text.Trim()), int.Parse(PlcPort.Text.Trim()));

                _remoteUdp = new xxUDPSyncServer(IPAddress.Parse(Plc_oneIp.Text.Trim()), int.Parse(Plc_onePort.Text.Trim()), Logger);
                _remoteUdp.DataReceived += (object? sender, byte[] dataBytes) =>
                {
                    if (_localUdp == null || _localUdp1 == null) return;
                    switch (dataBytes[66])
                    {
                        case 1:
                            if (dataBytes[3] == 1)
                            {
                                var newBytes = new byte[dataBytes.Length];
                                newBytes[3] = 1;
                                var data = dataBytes.Skip(34).Take(44 - 34).ToArray();
                                for (var i = 0; i < data.Length; i++)
                                {
                                    newBytes[34 + i] = data[i];
                                }
                                if (_aoi1PortEndPoint != null) _localUdp.SendAsync(_aoi1PortEndPoint, newBytes);
                                RemoteQueue?.Enqueue(dataBytes);
                            }
                            break;

                        case 2:
                            if (dataBytes[3] == 1)
                            {
                                var newBytes = new byte[dataBytes.Length];
                                newBytes[3] = 1;
                                var data = dataBytes.Skip(44).Take(54 - 44).ToArray();
                                for (var i = 0; i < data.Length; i++)
                                {
                                    newBytes[34 + i] = data[i];
                                }
                                if (_aoi2PortEndPoint != null) _localUdp1.SendAsync(_aoi2PortEndPoint, newBytes);
                                RemoteQueue?.Enqueue(dataBytes);
                            }
                            break;

                        case 3:
                            if (dataBytes[3] == 1)
                            {
                                var newBytes = new byte[dataBytes.Length];
                                newBytes[3] = 1;
                                var data = dataBytes.Skip(34).Take(44 - 34).ToArray();
                                for (var i = 0; i < data.Length; i++)
                                {
                                    newBytes[34 + i] = data[i];
                                }
                                var newBytes1 = new byte[128];
                                newBytes1[3] = 1;
                                var data1 = dataBytes.Skip(44).Take(54 - 44).ToArray();
                                for (var i = 0; i < data1.Length; i++)
                                {
                                    newBytes1[34 + i] = data1[i];
                                }

                                if (_aoi1PortEndPoint != null) _localUdp.SendAsync(_aoi1PortEndPoint, newBytes);
                                if (_aoi2PortEndPoint != null) _localUdp1.SendAsync(_aoi2PortEndPoint, newBytes1);
                                RemoteQueue?.Enqueue(dataBytes);
                            }
                            break;

                        default:
                            break;
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
                _localUdp = new xxUDPSyncServer(IPAddress.Parse(Aoi1_oneIp.Text.Trim()),
                    int.Parse(Aoi_onePort.Text.Trim()), Logger);
                _localUdp.DataReceived += (o, bytes) =>
                {
                    Logger.Info("接收AOI1消息:");
                    Logger.Info(bytes);
                    Logger.Info("-------------------------");
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

                MessageBox.Show(exception.Message);
            }
        }

        private void button2_Click_1(object sender, EventArgs e)
        {
            try
            {
                if (Logger == null) return;
                button2.Enabled = false;
                button5.Enabled = true;
                _aoi2PortEndPoint =
                    new IPEndPoint(IPAddress.Parse(Aoi2Ip.Text.Trim()), int.Parse(Aoi2Port.Text.Trim()));
                _localUdp1 = new xxUDPSyncServer(IPAddress.Parse(Aoi2_oneIp.Text.Trim()),
                    int.Parse(Aoi2_onePort.Text.Trim()), Logger);
                _localUdp1.DataReceived += (o, bytes) =>
                {
                    Logger.Info("接收AOI2消息:");
                    Logger.Info(bytes);
                    Logger.Info("-------------------------");
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
                try
                {
                    long timeOut;
                    DateTime beforeDt = default;
                    //拍照中
                    var inPhoto = true;
                    //拍照完成
                    var photoCompleted = true;
                    //检测完成
                    var complete = true;
                    //收到的消息
                    byte[] value = default;
                    if (RemoteQueue == null || _plcIpEndPoint == null || RemoveQueue == null)
                    {
                        continue;
                    }
                    RemoteQueue.Dequeue(out value);
                    Logger?.Info(value);
                    switch (value[66])
                    {
                        case 1:
                            var destination1 = value.Skip(34).Take(44 - 34).ToArray();
                            timeOut = 0;
                            beforeDt = DateTime.Now;
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
                                        var re = new byte[value.Length];
                                        Array.Copy(value, re, value.Length);
                                        re[1] = 1;
                                        re[2] = 0;
                                        re[3] = 0;
                                        _remoteUdp?.SendAsync(_plcIpEndPoint, re);
                                        Logger?.Info("拍照中O->PLC:");
                                        Logger?.Info(re);
                                        Logger?.Info("-------------------------");
                                        inPhoto = false;
                                        Aoi1Message.RemoveAll(item => item.SequenceEqual(a));
                                    }

                                    //拍照完成
                                    var b = Aoi1Message.Find(item =>
                                           item[2] == 1 && item.Skip(34).Take(44 - 34).ToArray().SequenceEqual(destination1)
                                       );
                                    if (b != null)
                                    {
                                        var re = new byte[value.Length];
                                        Array.Copy(value, re, value.Length);
                                        re[1] = 1;
                                        re[2] = 1;
                                        re[3] = 0;
                                        _remoteUdp?.SendAsync(_plcIpEndPoint, re);
                                        Logger?.Info("拍照完成O->PLC:");
                                        Logger?.Info(re);
                                        Logger?.Info("-------------------------");
                                        photoCompleted = false;
                                        Aoi1Message.RemoveAll(item => item.SequenceEqual(b));
                                    }

                                    //检测完成
                                    var c = Aoi1Message.Find(item =>
                                          item[2] == 2 && item.Skip(34).Take(44 - 34).ToArray().SequenceEqual(destination1)
                                      );
                                    if (c != null)
                                    {
                                        var re = new byte[value.Length];
                                        Array.Copy(value, re, value.Length);
                                        re[1] = 1;
                                        re[2] = 2;
                                        re[3] = 0;
                                        re[9] = c[9];
                                        re[10] = c[10];
                                        _remoteUdp?.SendAsync(_plcIpEndPoint, re);
                                        Logger?.Info("发送结果O->PLC:");
                                        Logger?.Info(re);
                                        Logger?.Info("-------------------------");
                                        complete = false;
                                        Aoi1Message.RemoveAll(item => item.SequenceEqual(c));
                                    }
                                    var afterDt = DateTime.Now;
                                    var ts = afterDt.Subtract(beforeDt);
                                    timeOut = ts.Milliseconds;
                                }
                            }
                            RemoveQueue.Enqueue(value);
                            break;

                        case 2:
                            var destination2 = value.Skip(44).Take(54 - 44).ToArray();
                            timeOut = 0;
                            beforeDt = DateTime.Now;
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
                                        var re = new byte[value.Length];
                                        Array.Copy(value, re, value.Length);
                                        re[1] = 1;
                                        re[2] = 0;
                                        re[3] = 0;
                                        _remoteUdp?.SendAsync(_plcIpEndPoint, re);
                                        Logger?.Info("拍照中O->PLC:");
                                        Logger?.Info(re);
                                        Logger?.Info("-------------------------");
                                        inPhoto = false;
                                        Aoi2Message.RemoveAll(item => item.SequenceEqual(a));
                                    }

                                    //拍照完成
                                    var b = Aoi2Message.Find(item =>
                                        item[2] == 1 && item.Skip(34).Take(44 - 34).ToArray().SequenceEqual(destination2)
                                    );
                                    if (b != null)
                                    {
                                        var re = new byte[value.Length];
                                        Array.Copy(value, re, value.Length);
                                        re[1] = 1;
                                        re[2] = 1;
                                        re[3] = 0;
                                        _remoteUdp?.SendAsync(_plcIpEndPoint, re);
                                        Logger?.Info("拍照完成O->PLC:");
                                        Logger?.Info(re);
                                        Logger?.Info("-------------------------");
                                        photoCompleted = false;
                                        Aoi2Message.RemoveAll(item => item.SequenceEqual(b));
                                    }

                                    //检测完成
                                    var c = Aoi2Message.Find(item =>
                                        item[2] == 2 && item.Skip(34).Take(44 - 34).ToArray().SequenceEqual(destination2)
                                    );
                                    if (c != null)
                                    {
                                        var re = new byte[value.Length];
                                        Array.Copy(value, re, value.Length);
                                        re[1] = 1;
                                        re[2] = 2;
                                        re[3] = 0;
                                        re[11] = c[9];
                                        re[12] = c[10];
                                        _remoteUdp?.SendAsync(_plcIpEndPoint, re);
                                        Logger?.Info("发送结果O->PLC:");
                                        Logger?.Info(re);
                                        Logger?.Info("-------------------------");
                                        complete = false;
                                        Aoi2Message.RemoveAll(item => item.SequenceEqual(c));
                                    }
                                    var afterDt = DateTime.Now;
                                    var ts = afterDt.Subtract(beforeDt);
                                    timeOut = ts.Milliseconds;
                                }
                            }
                            RemoveQueue.Enqueue(value);
                            break;

                        case 3:
                            var destination3 = value.Skip(34).Take(44 - 34).ToArray();
                            var destination4 = value.Skip(44).Take(54 - 44).ToArray();
                            timeOut = 0;
                            beforeDt = DateTime.Now;
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
                                    if (a != null && a1 != null)
                                    {
                                        //if (a != null && a1 != null)
                                        //{
                                        var re = new byte[value.Length];
                                        Array.Copy(value, re, value.Length);
                                        re[1] = 1;
                                        re[2] = 0;
                                        re[3] = 0;
                                        _remoteUdp?.SendAsync(_plcIpEndPoint, re);
                                        Logger?.Info("拍照中O->PLC");
                                        Logger?.Info(re);
                                        Logger?.Info("-------------------------");
                                        inPhoto = false;
                                        Aoi1Message.RemoveAll(item => item.SequenceEqual(a));
                                        Aoi2Message.RemoveAll(item => item.SequenceEqual(a1));
                                        //}
                                        //else if (a1 != null)
                                        //{
                                        //    byte[] re = new byte[value.Length];
                                        //    Array.Copy(value, re, value.Length);
                                        //    re[1] = 1;
                                        //    re[2] = 0;
                                        //    re[3] = 0;
                                        //    _remoteUdp.SendAsync(_plcIpEndPoint, re);
                                        //    log.Info("O->PLC:");
                                        //    log.Info(re);
                                        //    log.Info("-------------------------");
                                        //    inPhoto = false;
                                        //    //Aoi1Message.RemoveAll(item => item.SequenceEqual(a));
                                        //    Aoi2Message.RemoveAll(item => item.SequenceEqual(a1));
                                        //}
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
                                        var re = new byte[value.Length];
                                        Array.Copy(value, re, value.Length);
                                        re[1] = 1;
                                        re[2] = 1;
                                        re[3] = 0;
                                        _remoteUdp?.SendAsync(_plcIpEndPoint, re);
                                        Logger?.Info("拍照完成O->PLC:");
                                        Logger?.Info(re);
                                        Logger?.Info("-------------------------");
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
                                        var re = new byte[value.Length];
                                        Array.Copy(value, re, value.Length);
                                        re[1] = 1;
                                        re[2] = 2;
                                        re[3] = 0;
                                        re[9] = c[9];
                                        re[10] = c[10];
                                        re[11] = c1[9];
                                        re[12] = c1[10];
                                        _remoteUdp?.SendAsync(_plcIpEndPoint, re);
                                        Logger?.Info("发送结果O->PLC:");
                                        Logger?.Info(re);
                                        Logger?.Info("-------------------------");
                                        complete = false;
                                        Aoi1Message.RemoveAll(item => item.SequenceEqual(c));
                                        Aoi2Message.RemoveAll(item => item.SequenceEqual(c1));
                                    }
                                    var afterDt = DateTime.Now;
                                    var ts = afterDt.Subtract(beforeDt);
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
                try
                {
                    long timeOut;
                    DateTime beforeDt = default;
                    //拍照中
                    var inPhoto = true;
                    //拍照完成
                    var photoCompleted = true;
                    //检测完成
                    var complete = true;
                    //收到的消息
                    byte[] value = default;
                    if (RemoteQueue == null || _plcIpEndPoint == null || RemoveQueue == null)
                    {
                        continue;
                    }
                    RemoteQueue.Dequeue(out value);
                    Logger?.Info(value);
                    switch (value[66])
                    {
                        case 1:
                            var destination1 = value.Skip(34).Take(44 - 34).ToArray();
                            timeOut = 0;
                            beforeDt = DateTime.Now;
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
                                        var re = new byte[value.Length];
                                        Array.Copy(value, re, value.Length);
                                        re[1] = 1;
                                        re[2] = 0;
                                        re[3] = 0;
                                        _remoteUdp?.SendAsync(_plcIpEndPoint, re);
                                        Logger?.Info("拍照中O->PLC:");
                                        Logger?.Info(re);
                                        Logger?.Info("-------------------------");
                                        inPhoto = false;
                                        Aoi1Message.RemoveAll(item => item.SequenceEqual(a));
                                    }

                                    //拍照完成
                                    var b = Aoi1Message.Find(item =>
                                           item[2] == 1 && item.Skip(34).Take(44 - 34).ToArray().SequenceEqual(destination1)
                                       );
                                    if (b != null)
                                    {
                                        var re = new byte[value.Length];
                                        Array.Copy(value, re, value.Length);
                                        re[1] = 1;
                                        re[2] = 1;
                                        re[3] = 0;
                                        _remoteUdp?.SendAsync(_plcIpEndPoint, re);
                                        Logger?.Info("拍照完成O->PLC:");
                                        Logger?.Info(re);
                                        Logger?.Info("-------------------------");
                                        photoCompleted = false;
                                        Aoi1Message.RemoveAll(item => item.SequenceEqual(b));
                                    }

                                    //检测完成
                                    var c = Aoi1Message.Find(item =>
                                          item[2] == 2 && item.Skip(34).Take(44 - 34).ToArray().SequenceEqual(destination1)
                                      );
                                    if (c != null)
                                    {
                                        var re = new byte[value.Length];
                                        Array.Copy(value, re, value.Length);
                                        re[1] = 1;
                                        re[2] = 2;
                                        re[3] = 0;
                                        if (c[11] == 2)
                                        {
                                            re[9] = 2;
                                        }
                                        else
                                        {
                                            re[9] = 1;
                                        }
                                        //re[9] = c[9];
                                        re[12] = c[12];
                                        _remoteUdp?.SendAsync(_plcIpEndPoint, re);
                                        Logger?.Info("发送结果O->PLC:");
                                        Logger?.Info(re);
                                        Logger?.Info("-------------------------");
                                        complete = false;
                                        Aoi1Message.RemoveAll(item => item.SequenceEqual(c));
                                    }
                                    var afterDt = DateTime.Now;
                                    var ts = afterDt.Subtract(beforeDt);
                                    timeOut = ts.Milliseconds;
                                }
                            }
                            RemoveQueue.Enqueue(value);
                            break;

                        case 2:
                            var destination2 = value.Skip(44).Take(54 - 44).ToArray();
                            timeOut = 0;
                            beforeDt = DateTime.Now;
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
                                        var re = new byte[value.Length];
                                        Array.Copy(value, re, value.Length);
                                        re[1] = 1;
                                        re[2] = 0;
                                        re[3] = 0;
                                        _remoteUdp?.SendAsync(_plcIpEndPoint, re);
                                        Logger?.Info("拍照中O->PLC:");
                                        Logger?.Info(re);
                                        Logger?.Info("-------------------------");
                                        inPhoto = false;
                                        Aoi2Message.RemoveAll(item => item.SequenceEqual(a));
                                    }

                                    //拍照完成
                                    var b = Aoi2Message.Find(item =>
                                        item[2] == 1 && item.Skip(34).Take(44 - 34).ToArray().SequenceEqual(destination2)
                                    );
                                    if (b != null)
                                    {
                                        var re = new byte[value.Length];
                                        Array.Copy(value, re, value.Length);
                                        re[1] = 1;
                                        re[2] = 1;
                                        re[3] = 0;
                                        _remoteUdp?.SendAsync(_plcIpEndPoint, re);
                                        Logger?.Info("拍照完成O->PLC:");
                                        Logger?.Info(re);
                                        Logger?.Info("-------------------------");
                                        photoCompleted = false;
                                        Aoi2Message.RemoveAll(item => item.SequenceEqual(b));
                                    }

                                    //检测完成
                                    var c = Aoi2Message.Find(item =>
                                        item[2] == 2 && item.Skip(34).Take(44 - 34).ToArray().SequenceEqual(destination2)
                                    );
                                    if (c != null)
                                    {
                                        var re = new byte[value.Length];
                                        Array.Copy(value, re, value.Length);
                                        re[1] = 1;
                                        re[2] = 2;
                                        re[3] = 0;
                                        if (c[11] == 2)
                                        {
                                            re[9] = 2;
                                        }
                                        else
                                        {
                                            re[9] = 1;
                                        }
                                        re[11] = c[12];
                                        //re[12] = c[11];
                                        _remoteUdp?.SendAsync(_plcIpEndPoint, re);
                                        Logger?.Info("发送结果O->PLC:");
                                        Logger?.Info(re);
                                        Logger?.Info("-------------------------");
                                        complete = false;
                                        Aoi2Message.RemoveAll(item => item.SequenceEqual(c));
                                    }
                                    var afterDt = DateTime.Now;
                                    var ts = afterDt.Subtract(beforeDt);
                                    timeOut = ts.Milliseconds;
                                }
                            }
                            RemoveQueue.Enqueue(value);
                            break;

                        case 3:
                            var destination3 = value.Skip(34).Take(44 - 34).ToArray();
                            var destination4 = value.Skip(44).Take(54 - 44).ToArray();
                            timeOut = 0;
                            beforeDt = DateTime.Now;
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
                                    if (a != null && a1 != null)
                                    {
                                        //if (a != null && a1 != null)
                                        //{
                                        var re = new byte[value.Length];
                                        Array.Copy(value, re, value.Length);
                                        re[1] = 1;
                                        re[2] = 0;
                                        re[3] = 0;
                                        _remoteUdp?.SendAsync(_plcIpEndPoint, re);
                                        Logger?.Info("拍照中O->PLC");
                                        Logger?.Info(re);
                                        Logger?.Info("-------------------------");
                                        inPhoto = false;
                                        Aoi1Message.RemoveAll(item => item.SequenceEqual(a));
                                        Aoi2Message.RemoveAll(item => item.SequenceEqual(a1));
                                        //}
                                        //else if (a1 != null)
                                        //{
                                        //    byte[] re = new byte[value.Length];
                                        //    Array.Copy(value, re, value.Length);
                                        //    re[1] = 1;
                                        //    re[2] = 0;
                                        //    re[3] = 0;
                                        //    _remoteUdp.SendAsync(_plcIpEndPoint, re);
                                        //    log.Info("O->PLC:");
                                        //    log.Info(re);
                                        //    log.Info("-------------------------");
                                        //    inPhoto = false;
                                        //    //Aoi1Message.RemoveAll(item => item.SequenceEqual(a));
                                        //    Aoi2Message.RemoveAll(item => item.SequenceEqual(a1));
                                        //}
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
                                        var re = new byte[value.Length];
                                        Array.Copy(value, re, value.Length);
                                        re[1] = 1;
                                        re[2] = 1;
                                        re[3] = 0;
                                        _remoteUdp?.SendAsync(_plcIpEndPoint, re);
                                        Logger?.Info("拍照完成O->PLC:");
                                        Logger?.Info(re);
                                        Logger?.Info("-------------------------");
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
                                        var re = new byte[value.Length];
                                        Array.Copy(value, re, value.Length);
                                        re[1] = 1;
                                        re[2] = 2;
                                        re[3] = 0;
                                        if (c[11] == 2 || c1[11] == 2)
                                        {
                                            re[9] = 2;
                                        }
                                        else
                                        {
                                            re[9] = 1;
                                        }
                                        //re[10] = c[10];
                                        re[11] = c1[12];
                                        re[12] = c[12];
                                        _remoteUdp?.SendAsync(_plcIpEndPoint, re);
                                        Logger?.Info("发送结果O->PLC:");
                                        Logger?.Info(re);
                                        Logger?.Info("-------------------------");
                                        complete = false;
                                        Aoi1Message.RemoveAll(item => item.SequenceEqual(c));
                                        Aoi2Message.RemoveAll(item => item.SequenceEqual(c1));
                                    }
                                    var afterDt = DateTime.Now;
                                    var ts = afterDt.Subtract(beforeDt);
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
                    Logger?.Error("返回消息给PLC出错:" + e.Message, e);
                }
            }
        }

        #endregion 出版

        /// <summary>
        /// 转发AOI发送的检测清除信号
        /// </summary>
        private void Remove()
        {
            while (true)
            {
                try
                {
                    long timeOut;
                    DateTime beforeDt = default;
                    byte[] value = default;
                    var clear = true;
                    var complete = true;
                    if (RemoveQueue == null || _plcIpEndPoint == null)
                    {
                        continue;
                    }
                    RemoveQueue.Dequeue(out value);
                    switch (value[66])
                    {
                        case 1:
                            var destination1 = value.Skip(34).Take(44 - 34).ToArray();
                            timeOut = 0;
                            beforeDt = DateTime.Now;
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
                                        var re = new byte[value.Length];
                                        Array.Copy(value, re, value.Length);
                                        re[1] = 0;
                                        re[2] = 3;
                                        re[3] = 1;
                                        _remoteUdp?.SendAsync(_plcIpEndPoint, re);
                                        Logger?.Info(re);
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
                                        re[3] = 1;
                                        _remoteUdp?.SendAsync(_plcIpEndPoint, re);
                                        Logger?.Info(re);
                                        complete = false;
                                        Aoi1Message.RemoveAll(item => item.SequenceEqual(b));
                                    }

                                    var afterDt = DateTime.Now;
                                    var ts = afterDt.Subtract(beforeDt);
                                    timeOut = ts.Milliseconds;
                                }
                            }

                            break;

                        case 2:
                            var destination2 = value.Skip(44).Take(54 - 44).ToArray();
                            timeOut = 0;
                            beforeDt = DateTime.Now;
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
                                        var re = new byte[value.Length];
                                        Array.Copy(value, re, value.Length);
                                        re[1] = 0;
                                        re[2] = 3;
                                        re[3] = 0;
                                        _remoteUdp?.SendAsync(_plcIpEndPoint, re);
                                        Logger?.Info(re);
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
                                        _remoteUdp?.SendAsync(_plcIpEndPoint, re);
                                        Logger?.Info(re);
                                        complete = false;
                                        Aoi2Message.RemoveAll(item => item.SequenceEqual(b));
                                    }

                                    var afterDt = DateTime.Now;
                                    var ts = afterDt.Subtract(beforeDt);
                                    timeOut = ts.Milliseconds;
                                }
                            }

                            break;

                        case 3:
                            var destination3 = value.Skip(34).Take(44 - 34).ToArray();
                            var destination4 = value.Skip(44).Take(54 - 44).ToArray();
                            timeOut = 0;
                            beforeDt = DateTime.Now;
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
                                        var re = new byte[value.Length];
                                        Array.Copy(value, re, value.Length);
                                        re[1] = 0;
                                        re[2] = 3;
                                        re[3] = 0;
                                        _remoteUdp?.SendAsync(_plcIpEndPoint, re);
                                        Logger?.Info(re);
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
                                        var re = new byte[value.Length];
                                        Array.Copy(value, re, value.Length);
                                        re[1] = 1;
                                        re[2] = 3;
                                        re[3] = 0;
                                        _remoteUdp?.SendAsync(_plcIpEndPoint, re);
                                        Logger?.Info(re);
                                        complete = false;
                                        Aoi1Message.RemoveAll(item => item.SequenceEqual(b));
                                        Aoi2Message.RemoveAll(item => item.SequenceEqual(b1));
                                    }
                                    var afterDt = DateTime.Now;
                                    var ts = afterDt.Subtract(beforeDt);
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
                    Logger?.Error(e.Message, e);
                }
            }
        }

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
    }
}
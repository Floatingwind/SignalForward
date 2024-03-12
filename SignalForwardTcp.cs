using SignalForward.TCP;
using SignalForward.UDP;
using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Message = SignalForward.TCP.Message;

namespace SignalForward
{
    public partial class SignalForwardTcp : Form
    {
        public log4net.ILog? Logger;

        /// <summary>
        /// 远程tcpClient对象
        /// </summary>
        private SimpleTcpClient? _remoteTcp;

        ///// <summary>
        ///// PLC
        ///// </summary>
        //private IPEndPoint? _plcIpEndPoint;

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
        public WhCurrentQueue<byte[]>? RemoteQueue;

        /// <summary>
        /// 待删除的信号
        /// </summary>
        public WhCurrentQueue<byte[]>? RemoveQueue;

        /// <summary>
        /// AOI1发送的消息
        /// </summary>
        public List<byte[]> Aoi1Message = new();

        /// <summary>
        /// AOI2发送的消息
        /// </summary>
        public List<byte[]> Aoi2Message = new();

        /// <summary>
        /// 自旋锁
        /// </summary>
        public SpinLock SpinLock = new();

        /// <summary>
        /// 自旋锁1
        /// </summary>
        public SpinLock SpinLock1 = new();

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

        private static int _timeout;

        public SignalForwardTcp()
        {
            Logger = log4net.LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
            RemoteQueue = new WhCurrentQueue<byte[]>("等待发送结果", Logger);
            RemoveQueue = new WhCurrentQueue<byte[]>("等待删除结果", Logger);
            InitializeComponent();
            tcpPlcIp.DataBindings.Add("Enabled", tcpRemoteBnt, "Enabled");
            tcpPlcPort.DataBindings.Add("Enabled", tcpRemoteBnt, "Enabled");
            tcpPlc_oneIp.DataBindings.Add("Enabled", tcpRemoteBnt, "Enabled");
            tcpPlc_onePort.DataBindings.Add("Enabled", tcpRemoteBnt, "Enabled");

            tcpAoi1_oneIp.DataBindings.Add("Enabled", tcpButton1, "Enabled");
            tcpAoi_onePort.DataBindings.Add("Enabled", tcpButton1, "Enabled");
            tcpAoi1Ip.DataBindings.Add("Enabled", tcpButton1, "Enabled");
            tcpAoi1Port.DataBindings.Add("Enabled", tcpButton1, "Enabled");

            tcpAoi2_oneIp.DataBindings.Add("Enabled", tcpButton2, "Enabled");
            tcpAoi2_onePort.DataBindings.Add("Enabled", tcpButton2, "Enabled");
            tcpAoi2Ip.DataBindings.Add("Enabled", tcpButton2, "Enabled");
            tcpAoi2Port.DataBindings.Add("Enabled", tcpButton2, "Enabled");
            tcpNumericUpDown1.DataBindings.Add("Enabled", tcpButton2, "Enabled");
            InitParam();
            Task.Factory.StartNew(Remove, _tokenSource2.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
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
                                byte[] buff = _localUdp.Server.Receive(ref rEndPoint);
                                if (buff != default)
                                {
                                    _remoteTcp.Write(buff);
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
                                byte[] buff = _localUdp1.Server.Receive(ref rEndPoint);
                                if (buff != default)
                                {
                                    _remoteTcp.Write(buff);
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
                                    return _localUdp.Server.Receive(ref ipEnd);
                                }));
                                tasks.Add(result);
                                Task<byte[]> result1 = new Task<byte[]>((() =>
                                {
                                    IPEndPoint ipEnd = default;
                                    return _localUdp1.Server.Receive(ref ipEnd);
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

                tcpRemoteBnt.Enabled = false;
                tcpButton3.Enabled = true;
                //_plcIpEndPoint = new IPEndPoint(IPAddress.Parse(tcpPlcIp.Text.Trim()), int.Parse(tcpPlcPort.Text.Trim()));

                _remoteTcp = new SimpleTcpClient();
                _remoteTcp.Connect(tcpPlcIp.Text, int.Parse(tcpPlcPort.Text.Trim()));

                _remoteTcp.DataReceived += (object? sender, Message message) =>
                {
                    if (_localUdp == null || _localUdp1 == null) return;
                    if (message.Data[1] != 1)
                    {
                        Logger.Info($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}无触发信号:");
                        Logger.Info(message.Data);
                        Logger.Info("-------------------------");
                        return;
                    }

                    //var t = Encoding.ASCII.GetString(message.Data, 34, 6);
                    //var t1 = Encoding.ASCII.GetString(message.Data, 84, 6);

                    var template = new byte[] { 0, 0, 0, 0, 0, 0 };

                    if ((!message.Data.Skip(34).Take(6).SequenceEqual(template)) == true && (!message.Data.Skip(84).Take(6).SequenceEqual(template)) == false)
                    {
                        var newBytes = new byte[message.Data.Length];
                        newBytes[3] = 1;
                        message.Data[2] = 1;
                        var data = message.Data.Skip(34).Take(10).ToArray();
                        for (var i = 0; i < data.Length; i++)
                        {
                            newBytes[34 + i] = data[i];
                        }
                        if (_aoi1PortEndPoint != null) _localUdp.SendAsync(_aoi1PortEndPoint, newBytes);
                        RemoteQueue?.Enqueue(message.Data);
                    }
                    else if ((!message.Data.Skip(34).Take(6).SequenceEqual(template)) == false && (!message.Data.Skip(84).Take(6).SequenceEqual(template)) == true)
                    {
                        var newBytes = new byte[message.Data.Length];
                        newBytes[3] = 1;
                        message.Data[2] = 2;
                        var data = message.Data.Skip(84).Take(10).ToArray();
                        for (var i = 0; i < data.Length; i++)
                        {
                            newBytes[34 + i] = data[i];
                        }
                        if (_aoi2PortEndPoint != null) _localUdp1.SendAsync(_aoi2PortEndPoint, newBytes);
                        RemoteQueue?.Enqueue(message.Data);
                    }
                    else if ((!message.Data.Skip(34).Take(6).SequenceEqual(template)) == true && (!message.Data.Skip(84).Take(6).SequenceEqual(template)) == true)
                    {
                        var newBytes = new byte[message.Data.Length];
                        newBytes[3] = 1;
                        //newBytes[30] = 1;
                        var data = message.Data.Skip(34).Take(10).ToArray();
                        for (var i = 0; i < data.Length; i++)
                        {
                            newBytes[34 + i] = data[i];
                        }
                        var newBytes1 = new byte[message.Data.Length];
                        newBytes1[3] = 1;
                        //newBytes1[30] = 2;
                        var data1 = message.Data.Skip(84).Take(10).ToArray();
                        for (var i = 0; i < data1.Length; i++)
                        {
                            newBytes1[34 + i] = data1[i];
                        }
                        message.Data[2] = 3;
                        if (_aoi1PortEndPoint != null) _localUdp.SendAsync(_aoi1PortEndPoint, newBytes);
                        if (_aoi2PortEndPoint != null) _localUdp1.SendAsync(_aoi2PortEndPoint, newBytes1);
                        RemoteQueue?.Enqueue(message.Data);
                    }
                    else
                    {
                        Logger.Info($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}半片标识为0:");
                        Logger.Info(message.Data);
                        Logger.Info("-------------------------");
                        return;
                    }

                    //switch (message.Data[2])
                    //{
                    //    case 1:
                    //        if (message.Data[1] == 1)
                    //        {
                    //            var newBytes = new byte[message.Data.Length];
                    //            newBytes[3] = 1;
                    //            newBytes[30] = message.Data[2];
                    //            var data = message.Data.Skip(45).Take(10).ToArray();
                    //            for (var i = 0; i < data.Length; i++)
                    //            {
                    //                newBytes[34 + i] = data[i];
                    //            }
                    //            if (_aoi1PortEndPoint != null) _localUdp.SendAsync(_aoi1PortEndPoint, newBytes);
                    //            RemoteQueue?.Enqueue(message.Data);
                    //        }
                    //        break;

                    //    case 2:
                    //        if (message.Data[1] == 1)
                    //        {
                    //            var newBytes = new byte[message.Data.Length];
                    //            newBytes[3] = 1;
                    //            newBytes[30] = message.Data[2];
                    //            var data = message.Data.Skip(45).Take(10).ToArray();
                    //            for (var i = 0; i < data.Length; i++)
                    //            {
                    //                newBytes[34 + i] = data[i];
                    //            }
                    //            if (_aoi2PortEndPoint != null) _localUdp1.SendAsync(_aoi2PortEndPoint, newBytes);
                    //            RemoteQueue?.Enqueue(message.Data);
                    //        }
                    //        break;

                    //    case 3:
                    //        if (message.Data[1] == 1)
                    //        {
                    //            var newBytes = new byte[message.Data.Length];
                    //            newBytes[3] = 1;
                    //            newBytes[30] = 1;
                    //            var data = message.Data.Skip(45).Take(10).ToArray();
                    //            for (var i = 0; i < data.Length; i++)
                    //            {
                    //                newBytes[34 + i] = data[i];
                    //            }
                    //            var newBytes1 = new byte[message.Data.Length];
                    //            newBytes1[3] = 1;
                    //            newBytes1[30] = 2;
                    //            var data1 = message.Data.Skip(45).Take(10).ToArray();
                    //            for (var i = 0; i < data1.Length; i++)
                    //            {
                    //                newBytes1[34 + i] = data1[i];
                    //            }

                    //            if (_aoi1PortEndPoint != null) _localUdp.SendAsync(_aoi1PortEndPoint, newBytes);
                    //            if (_aoi2PortEndPoint != null) _localUdp1.SendAsync(_aoi2PortEndPoint, newBytes1);
                    //            RemoteQueue?.Enqueue(message.Data);
                    //        }
                    //        break;

                    //    default:
                    //        Logger.Info($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}半片标识为0:");
                    //        Logger.Info(message.Data);
                    //        Logger.Info("-------------------------");
                    //        break;
                    //}
                };
            }
            catch (Exception exception)
            {
                tcpRemoteBnt.Enabled = true;
                tcpButton3.Enabled = false;
                if (_remoteTcp != null)
                {
                    _remoteTcp.Disconnect();
                    _remoteTcp.Dispose();
                    _remoteTcp = null;
                }

                MessageBox.Show(exception.Message);
            }
        }

        private void button1_Click(object sender, EventArgs e)
        {
            try
            {
                if (Logger == null) return;
                tcpButton1.Enabled = false;
                tcpButton4.Enabled = true;
                _aoi1PortEndPoint =
                    new IPEndPoint(IPAddress.Parse(tcpAoi1Ip.Text.Trim()), int.Parse(tcpAoi1Port.Text.Trim()));
                _localUdp = new UdpSyncServer(IPAddress.Parse(tcpAoi1_oneIp.Text.Trim()),
                    int.Parse(tcpAoi_onePort.Text.Trim()), Logger);
                _localUdp.DataReceived += (o, bytes) =>
                {
                    Logger.Info($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}接收AOI1消息:");
                    Logger.Info(bytes);
                    Logger.Info("-------------------------");
                    LockMethod(() => { Aoi1Message.Add(bytes); });
                };
                _localUdp.Start();
            }
            catch (Exception exception)
            {
                tcpButton1.Enabled = true;
                tcpButton4.Enabled = false;
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
                if (tcpCP.Checked)
                {
                    tcpCB.Enabled = false;
                }
                else if (tcpCP.Checked == false && tcpCB.Checked == false)
                {
                    MessageBox.Show("请选择通讯模式！");
                    return;
                }
                else
                {
                    tcpCP.Enabled = false;
                }

                if (Logger == null) return;
                tcpButton2.Enabled = false;
                tcpButton5.Enabled = true;
                _aoi2PortEndPoint =
                    new IPEndPoint(IPAddress.Parse(tcpAoi2Ip.Text.Trim()), int.Parse(tcpAoi2Port.Text.Trim()));
                _localUdp1 = new UdpSyncServer(IPAddress.Parse(tcpAoi2_oneIp.Text.Trim()),
                    int.Parse(tcpAoi2_onePort.Text.Trim()), Logger);
                _localUdp1.DataReceived += (o, bytes) =>
                {
                    Logger.Info($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}接收AOI2消息:");
                    Logger.Info(bytes);
                    Logger.Info("-------------------------");
                    LockMethod1(() => { Aoi2Message.Add(bytes); });
                };
                _localUdp1.Start();
                _timeout = ((int)tcpNumericUpDown1.Value);
            }
            catch (Exception exception)
            {
                tcpButton2.Enabled = true;
                tcpButton5.Enabled = false;
                if (_localUdp1 != null)
                {
                    _localUdp1.Stop();
                    _localUdp1.Dispose();
                    _localUdp1 = null;
                }

                MessageBox.Show(exception.Message);
            }
        }

        #region 正检

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
                    var inPhoto = true;
                    //拍照完成
                    var photoCompleted = true;
                    //检测完成
                    var complete = true;
                    //收到的消息
                    if (RemoteQueue == null || RemoveQueue == null || _localUdp == null || _localUdp1 == null)
                    {
                        continue;
                    }
                    RemoteQueue.Dequeue(out var value);
                    Logger?.Info(value);
                    switch (value[2])
                    {
                        case 1:
                            var destination1 = value.Skip(34).Take(10).ToArray();
                            timeOut = 0;
                            beforeDt = DateTime.Now;

                            while ((inPhoto || photoCompleted || complete) && timeOut < (_timeout != default ? _timeout : 600))
                            {
                                //拍照中
                                LockMethod(() =>
                                {
                                    var a = Aoi1Message.Find(item =>
                                        item[2] == 0 && item.Skip(34).Take(10).ToArray()
                                            .SequenceEqual(destination1)
                                    );

                                    if (a != null)
                                    {
                                        var re = new byte[value.Length];
                                        Array.Copy(value, re, value.Length);
                                        re[1] = 1;

                                        //_remoteTcp?.Write(re);
                                        Logger?.Info($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}拍照中O->PLC:");
                                        Logger?.Info(re);
                                        Logger?.Info("-------------------------");
                                        inPhoto = false;
                                        Aoi1Message.RemoveAll(item => item.SequenceEqual(a));
                                    }
                                });

                                //拍照完成
                                LockMethod(() =>
                                {
                                    var b = Aoi1Message.Find(item =>
                                        item[2] == 1 && item.Skip(34).Take(10).ToArray()
                                            .SequenceEqual(destination1)
                                    );
                                    if (b != null)
                                    {
                                        var re = new byte[value.Length];
                                        Array.Copy(value, re, value.Length);
                                        re[1] = 2;
                                        //re[2] = 1;
                                        //re[3] = 0;
                                        _remoteTcp?.Write(re);
                                        Logger?.Info($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}拍照完成O->PLC:");
                                        Logger?.Info(re);
                                        Logger?.Info("-------------------------");
                                        photoCompleted = false;
                                        Aoi1Message.RemoveAll(item => item.SequenceEqual(b));
                                    }
                                });

                                //检测完成
                                LockMethod(() =>
                                {
                                    var c = Aoi1Message.Find(item =>
                                       item[2] == 2 && item.Skip(34).Take(10).ToArray()
                                           .SequenceEqual(destination1)
                                   );
                                    if (c != null)
                                    {
                                        var re = new byte[value.Length];
                                        Array.Copy(value, re, value.Length);
                                        re[1] = 4;
                                        re[2] = 1;
                                        //re[3] = 0;
                                        re[45] = c[9];
                                        re[47] = c[10];
                                        _remoteTcp?.WriteAsync(re);
                                        Logger?.Info($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}发送结果O->PLC:");
                                        Logger?.Info(re);
                                        Logger?.Info("-------------------------");
                                        complete = false;
                                        Aoi1Message.RemoveAll(item => item.SequenceEqual(c));
                                    }
                                });

                                var afterDt = DateTime.Now;
                                var ts = afterDt.Subtract(beforeDt);
                                timeOut = ts.Ticks / 10000;
                            }
                            RemoveQueue.Enqueue(value);
                            break;

                        case 2:
                            var destination2 = value.Skip(84).Take(10).ToArray();
                            timeOut = 0;
                            beforeDt = DateTime.Now;
                            while ((inPhoto || photoCompleted || complete) && timeOut < (_timeout != default ? _timeout : 600))
                            {
                                //拍照中
                                LockMethod1(() =>
                                {
                                    var a = Aoi2Message.Find(item =>
                                        item[2] == 0 && item.Skip(34).Take(10).ToArray()
                                            .SequenceEqual(destination2)
                                    );
                                    if (a != null)
                                    {
                                        var re = new byte[value.Length];
                                        Array.Copy(value, re, value.Length);
                                        re[1] = 1;

                                        //_remoteTcp?.WriteAsync(re);
                                        Logger?.Info($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}拍照中O->PLC:");
                                        Logger?.Info(re);
                                        Logger?.Info("-------------------------");
                                        inPhoto = false;
                                        Aoi2Message.RemoveAll(item => item.SequenceEqual(a));
                                    }
                                });

                                //拍照完成
                                LockMethod1(() =>
                                {
                                    var b = Aoi2Message.Find(item =>
                                        item[2] == 1 && item.Skip(34).Take(10).ToArray()
                                            .SequenceEqual(destination2)
                                    );
                                    if (b != null)
                                    {
                                        var re = new byte[value.Length];
                                        Array.Copy(value, re, value.Length);
                                        re[1] = 2;
                                        //re[2] = 2;
                                        //re[3] = 0;
                                        _remoteTcp?.Write(re);
                                        Logger?.Info($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}拍照完成O->PLC:");
                                        Logger?.Info(re);
                                        Logger?.Info("-------------------------");
                                        photoCompleted = false;
                                        Aoi2Message.RemoveAll(item => item.SequenceEqual(b));
                                    }
                                });

                                //检测完成
                                LockMethod1(() =>
                                {
                                    var c = Aoi2Message.Find(item =>
                                        item[2] == 2 && item.Skip(34).Take(10).ToArray()
                                            .SequenceEqual(destination2)
                                    );
                                    if (c != null)
                                    {
                                        var re = new byte[value.Length];
                                        Array.Copy(value, re, value.Length);
                                        re[1] = 4;
                                        //re[2] = 2;
                                        //re[3] = 0;
                                        re[95] = c[9];
                                        re[97] = c[10];
                                        _remoteTcp?.Write(re);
                                        Logger?.Info($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}发送结果O->PLC:");
                                        Logger?.Info(re);
                                        Logger?.Info("-------------------------");
                                        complete = false;
                                        Aoi2Message.RemoveAll(item => item.SequenceEqual(c));
                                    }
                                });

                                var afterDt = DateTime.Now;
                                var ts = afterDt.Subtract(beforeDt);
                                timeOut = ts.Ticks / 10000;
                            }
                            RemoveQueue.Enqueue(value);
                            break;

                        case 3:
                            var destination3 = value.Skip(34).Take(10).ToArray();
                            var destination4 = value.Skip(84).Take(10).ToArray();
                            timeOut = 0;
                            beforeDt = DateTime.Now;
                            while ((inPhoto || photoCompleted || complete) && timeOut < (_timeout != default ? _timeout : 600))
                            {
                                byte[] a = default;
                                byte[] a1 = default;
                                byte[] b = default;
                                byte[] b1 = default;
                                byte[] c = default;
                                byte[] c1 = default;

                                //拍照中
                                LockMethod(() =>
                                {
                                    a = Aoi1Message.Find(item => item[2] == 0 && item.Skip(34).Take(10).ToArray().SequenceEqual(destination3));
                                });

                                LockMethod1(() =>
                                {
                                    a1 = Aoi2Message.Find(item => item[2] == 0 && item.Skip(34).Take(10).ToArray().SequenceEqual(destination4));
                                });

                                if (a != null && a1 != null)
                                {
                                    var re = new byte[value.Length];
                                    Array.Copy(value, re, value.Length);
                                    re[1] = 1;

                                    //_remoteTcp?.Write(re);
                                    Logger?.Info($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}拍照中O->PLC");
                                    Logger?.Info(re);
                                    Logger?.Info("-------------------------");
                                    inPhoto = false;

                                    LockMethod(() => { Aoi1Message.RemoveAll(item => item.SequenceEqual(a)); });
                                    LockMethod1(() => { Aoi2Message.RemoveAll(item => item.SequenceEqual(a1)); });
                                }

                                //拍照完成
                                LockMethod(() =>
                                {
                                    b = Aoi1Message.Find(item =>
                                    item[2] == 1 && item.Skip(34).Take(10).ToArray()
                                        .SequenceEqual(destination3)
                                );
                                });

                                LockMethod1(() =>
                                {
                                    b1 = Aoi2Message.Find(item =>
                                    item[2] == 1 && item.Skip(34).Take(10).ToArray()
                                        .SequenceEqual(destination4)
                                );
                                });

                                if (b != null && b1 != null)
                                {
                                    var re = new byte[value.Length];
                                    Array.Copy(value, re, value.Length);
                                    re[1] = 2;
                                    //re[2] = 3;
                                    //re[3] = 0;
                                    _remoteTcp?.Write(re);
                                    Logger?.Info($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}拍照完成O->PLC:");
                                    Logger?.Info(re);
                                    Logger?.Info("-------------------------");
                                    photoCompleted = false;

                                    LockMethod(() => { Aoi1Message.RemoveAll(item => item.SequenceEqual(b)); });
                                    LockMethod1(() => { Aoi2Message.RemoveAll(item => item.SequenceEqual(b1)); });
                                }

                                //检测完成
                                LockMethod(() =>
                                {
                                    c = Aoi1Message.Find(item =>
                                    item[2] == 2 && item.Skip(34).Take(10).ToArray()
                                        .SequenceEqual(destination3)
                                );
                                });

                                LockMethod1(() =>
                                {
                                    c1 = Aoi2Message.Find(item =>
                                    item[2] == 2 && item.Skip(34).Take(10).ToArray()
                                        .SequenceEqual(destination4)
                                );
                                });

                                if (c != null && c1 != null)
                                {
                                    var re = new byte[value.Length];
                                    Array.Copy(value, re, value.Length);
                                    re[1] = 4;

                                    re[45] = c[9];
                                    re[47] = c[10];

                                    re[95] = c1[9];
                                    re[97] = c1[10];
                                    _remoteTcp?.Write(re);

                                    Logger?.Info($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}发送结果O->PLC:");
                                    Logger?.Info(re);
                                    Logger?.Info("-------------------------");
                                    complete = false;

                                    LockMethod(() => { Aoi1Message.RemoveAll(item => item.SequenceEqual(c)); });
                                    LockMethod1(() => { Aoi2Message.RemoveAll(item => item.SequenceEqual(c1)); });
                                }

                                var afterDt = DateTime.Now;
                                var ts = afterDt.Subtract(beforeDt);
                                timeOut = ts.Ticks / 10000;
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

        #endregion 正检

        #region 背检

        /// <summary>
        /// 转发AOI发送的消息给PLC
        /// </summary>
        private void Transmit1()
        {
            while (true)
            {
                _tokenSource1?.Token.ThrowIfCancellationRequested();
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
                    if (RemoteQueue == null || RemoveQueue == null || _localUdp == null || _localUdp1 == null)
                    {
                        continue;
                    }
                    RemoteQueue.Dequeue(out var value);
                    Logger?.Info(value);
                    switch (value[2])
                    {
                        case 1:
                            var destination1 = value.Skip(34).Take(10).ToArray();
                            timeOut = 0;
                            beforeDt = DateTime.Now;

                            while ((inPhoto || photoCompleted || complete) && timeOut < (_timeout != default ? _timeout : 600))
                            {
                                //拍照中
                                LockMethod(() =>
                                {
                                    var a = Aoi1Message.Find(item =>
                                        item[2] == 0 && item.Skip(34).Take(10).ToArray()
                                            .SequenceEqual(destination1)
                                    );

                                    if (a != null)
                                    {
                                        var re = new byte[value.Length];
                                        Array.Copy(value, re, value.Length);
                                        re[1] = 1;

                                        //_remoteTcp?.Write(re);
                                        Logger?.Info($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}拍照中O->PLC:");
                                        Logger?.Info(re);
                                        Logger?.Info("-------------------------");
                                        inPhoto = false;
                                        Aoi1Message.RemoveAll(item => item.SequenceEqual(a));
                                    }
                                });

                                //拍照完成
                                LockMethod(() =>
                                {
                                    var b = Aoi1Message.Find(item =>
                                        item[2] == 1 && item.Skip(34).Take(10).ToArray()
                                            .SequenceEqual(destination1)
                                    );
                                    if (b != null)
                                    {
                                        var re = new byte[value.Length];
                                        Array.Copy(value, re, value.Length);
                                        re[1] = 2;

                                        _remoteTcp?.Write(re);
                                        Logger?.Info($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}拍照完成O->PLC:");
                                        Logger?.Info(re);
                                        Logger?.Info("-------------------------");
                                        photoCompleted = false;
                                        Aoi1Message.RemoveAll(item => item.SequenceEqual(b));
                                    }
                                });
                                //外观
                                LockMethod(() =>
                                {
                                    var c = Aoi1Message.Find(item =>
                                        item[2] == 3 && item.Skip(34).Take(10).ToArray()
                                            .SequenceEqual(destination1)
                                    );
                                    if (c != null)
                                    {
                                        var re = new byte[value.Length];
                                        Array.Copy(value, re, value.Length);

                                        re[1] = 4;

                                        re[49] = c[9];
                                        //re[51] = c[10];
                                        _remoteTcp?.WriteAsync(re);
                                        Logger?.Info($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}发送结果O->PLC:");
                                        Logger?.Info(re);
                                        Logger?.Info("-------------------------");
                                        complete = false;
                                        Aoi1Message.RemoveAll(item => item.SequenceEqual(c));
                                    }
                                });

                                //检测完成
                                LockMethod(() =>
                                {
                                    var c = Aoi1Message.Find(item =>
                                       item[2] == 2 && item.Skip(34).Take(10).ToArray()
                                           .SequenceEqual(destination1)
                                   );
                                    if (c != null)
                                    {
                                        var re = new byte[value.Length];
                                        Array.Copy(value, re, value.Length);

                                        re[1] = 4;

                                        re[49] = c[9];
                                        re[51] = c[10];
                                        _remoteTcp?.WriteAsync(re);
                                        Logger?.Info($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}发送结果O->PLC:");
                                        Logger?.Info(re);
                                        Logger?.Info("-------------------------");
                                        complete = false;
                                        Aoi1Message.RemoveAll(item => item.SequenceEqual(c));
                                    }
                                });

                                var afterDt = DateTime.Now;
                                var ts = afterDt.Subtract(beforeDt);
                                timeOut = ts.Ticks / 10000;
                            }
                            RemoveQueue.Enqueue(value);
                            break;

                        case 2:
                            var destination2 = value.Skip(84).Take(10).ToArray();
                            timeOut = 0;
                            beforeDt = DateTime.Now;
                            while ((inPhoto || photoCompleted || complete) && timeOut < (_timeout != default ? _timeout : 600))
                            {
                                //拍照中
                                LockMethod1(() =>
                                {
                                    var a = Aoi2Message.Find(item =>
                                        item[2] == 0 && item.Skip(34).Take(10).ToArray()
                                            .SequenceEqual(destination2)
                                    );
                                    if (a != null)
                                    {
                                        var re = new byte[value.Length];
                                        Array.Copy(value, re, value.Length);
                                        re[1] = 1;

                                        //_remoteTcp?.WriteAsync(re);
                                        Logger?.Info($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}拍照中O->PLC:");
                                        Logger?.Info(re);
                                        Logger?.Info("-------------------------");
                                        inPhoto = false;
                                        Aoi2Message.RemoveAll(item => item.SequenceEqual(a));
                                    }
                                });

                                //拍照完成
                                LockMethod1(() =>
                                {
                                    var b = Aoi2Message.Find(item =>
                                        item[2] == 1 && item.Skip(34).Take(10).ToArray()
                                            .SequenceEqual(destination2)
                                    );
                                    if (b != null)
                                    {
                                        var re = new byte[value.Length];
                                        Array.Copy(value, re, value.Length);
                                        re[1] = 2;

                                        _remoteTcp?.Write(re);
                                        Logger?.Info($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}拍照完成O->PLC:");
                                        Logger?.Info(re);
                                        Logger?.Info("-------------------------");
                                        photoCompleted = false;
                                        Aoi2Message.RemoveAll(item => item.SequenceEqual(b));
                                    }
                                });

                                //外观
                                LockMethod1(() =>
                                {
                                    var c = Aoi2Message.Find(item =>
                                        item[2] == 3 && item.Skip(34).Take(44 - 34).ToArray()
                                            .SequenceEqual(destination2)
                                    );
                                    if (c != null)
                                    {
                                        var re = new byte[value.Length];
                                        Array.Copy(value, re, value.Length);

                                        re[1] = 4;

                                        re[99] = c[9];
                                        //re[101] = c[10];
                                        _remoteTcp?.Write(re);

                                        Logger?.Info($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}发送结果O->PLC:");
                                        Logger?.Info(re);
                                        Logger?.Info("-------------------------");
                                        complete = false;
                                        Aoi2Message.RemoveAll(item => item.SequenceEqual(c));
                                    }
                                });

                                //检测完成
                                LockMethod1(() =>
                                {
                                    var c = Aoi2Message.Find(item =>
                                        item[2] == 2 && item.Skip(34).Take(44 - 34).ToArray()
                                            .SequenceEqual(destination2)
                                    );
                                    if (c != null)
                                    {
                                        var re = new byte[value.Length];
                                        Array.Copy(value, re, value.Length);
                                        //re[0] = 1;
                                        re[1] = 4;

                                        re[99] = c[9];
                                        re[101] = c[10];
                                        _remoteTcp?.Write(re);

                                        Logger?.Info($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}发送结果O->PLC:");
                                        Logger?.Info(re);
                                        Logger?.Info("-------------------------");
                                        complete = false;
                                        Aoi2Message.RemoveAll(item => item.SequenceEqual(c));
                                    }
                                });

                                var afterDt = DateTime.Now;
                                var ts = afterDt.Subtract(beforeDt);
                                timeOut = ts.Ticks / 10000;
                            }
                            RemoveQueue.Enqueue(value);
                            break;

                        case 3:
                            var destination3 = value.Skip(34).Take(10).ToArray();
                            var destination4 = value.Skip(84).Take(10).ToArray();
                            timeOut = 0;
                            beforeDt = DateTime.Now;
                            while ((inPhoto || photoCompleted || complete) && timeOut < (_timeout != default ? _timeout : 600))
                            {
                                byte[] a = default;
                                byte[] a1 = default;
                                byte[] b = default;
                                byte[] b1 = default;
                                byte[] c = default;
                                byte[] c1 = default;
                                byte[] d = default;
                                byte[] d1 = default;
                                //拍照中
                                LockMethod(() =>
                                {
                                    a = Aoi1Message.Find(item => item[2] == 0 && item.Skip(34).Take(44 - 34).ToArray().SequenceEqual(destination3));
                                });

                                LockMethod1(() =>
                                {
                                    a1 = Aoi2Message.Find(item => item[2] == 0 && item.Skip(34).Take(44 - 34).ToArray().SequenceEqual(destination4));
                                });

                                if (a != null && a1 != null)
                                {
                                    var re = new byte[value.Length];
                                    Array.Copy(value, re, value.Length);
                                    re[1] = 1;

                                    //_remoteTcp?.Write(re);
                                    Logger?.Info($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}拍照中O->PLC");
                                    Logger?.Info(re);
                                    Logger?.Info("-------------------------");
                                    inPhoto = false;

                                    LockMethod(() => { Aoi1Message.RemoveAll(item => item.SequenceEqual(a)); });
                                    LockMethod1(() => { Aoi2Message.RemoveAll(item => item.SequenceEqual(a1)); });
                                }

                                //拍照完成
                                LockMethod(() =>
                                {
                                    b = Aoi1Message.Find(item =>
                                    item[2] == 1 && item.Skip(34).Take(44 - 34).ToArray()
                                        .SequenceEqual(destination3)
                                );
                                });

                                LockMethod1(() =>
                                {
                                    b1 = Aoi2Message.Find(item =>
                                    item[2] == 1 && item.Skip(34).Take(44 - 34).ToArray()
                                        .SequenceEqual(destination4)
                                );
                                });

                                if (b != null && b1 != null)
                                {
                                    var re = new byte[value.Length];
                                    Array.Copy(value, re, value.Length);
                                    re[1] = 2;
                                    //re[2] = 3;
                                    //re[3] = 0;
                                    _remoteTcp?.Write(re);
                                    Logger?.Info($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}拍照完成O->PLC:");
                                    Logger?.Info(re);
                                    Logger?.Info("-------------------------");
                                    photoCompleted = false;

                                    LockMethod(() => { Aoi1Message.RemoveAll(item => item.SequenceEqual(b)); });
                                    LockMethod1(() => { Aoi2Message.RemoveAll(item => item.SequenceEqual(b1)); });
                                }

                                //外观
                                LockMethod(() =>
                                {
                                    d = Aoi1Message.Find(item =>
                                        item[2] == 3 && item.Skip(34).Take(44 - 34).ToArray()
                                            .SequenceEqual(destination3)
                                    );
                                });

                                LockMethod1(() =>
                                {
                                    d1 = Aoi2Message.Find(item =>
                                        item[2] == 3 && item.Skip(34).Take(44 - 34).ToArray()
                                            .SequenceEqual(destination4)
                                    );
                                });

                                if (d != null && d1 != null)
                                {
                                    var re = new byte[value.Length];
                                    Array.Copy(value, re, value.Length);
                                    //re[0] = 1;
                                    re[1] = 4;
                                    re[49] = c[9];
                                    //re[51] = c[10];
                                    re[99] = c1[9];
                                    //re[101] = c1[10];
                                    _remoteTcp?.Write(re);

                                    Logger?.Info($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}发送结果O->PLC:");
                                    Logger?.Info(re);
                                    Logger?.Info("-------------------------");
                                    complete = false;

                                    LockMethod(() => { Aoi1Message.RemoveAll(item => item.SequenceEqual(c)); });
                                    LockMethod1(() => { Aoi2Message.RemoveAll(item => item.SequenceEqual(c1)); });
                                }

                                //检测完成
                                LockMethod(() =>
                                {
                                    c = Aoi1Message.Find(item =>
                                    item[2] == 2 && item.Skip(34).Take(44 - 34).ToArray()
                                        .SequenceEqual(destination3)
                                );
                                });

                                LockMethod1(() =>
                                {
                                    c1 = Aoi2Message.Find(item =>
                                    item[2] == 2 && item.Skip(34).Take(44 - 34).ToArray()
                                        .SequenceEqual(destination4)
                                );
                                });

                                if (c != null && c1 != null)
                                {
                                    var re = new byte[value.Length];
                                    Array.Copy(value, re, value.Length);
                                    //re[0] = 1;
                                    re[1] = 4;
                                    re[49] = c[9];
                                    re[51] = c[10];
                                    re[99] = c1[9];
                                    re[101] = c1[10];
                                    _remoteTcp?.Write(re);

                                    Logger?.Info($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}发送结果O->PLC:");
                                    Logger?.Info(re);
                                    Logger?.Info("-------------------------");
                                    complete = false;

                                    LockMethod(() => { Aoi1Message.RemoveAll(item => item.SequenceEqual(c)); });
                                    LockMethod1(() => { Aoi2Message.RemoveAll(item => item.SequenceEqual(c1)); });
                                }

                                var afterDt = DateTime.Now;
                                var ts = afterDt.Subtract(beforeDt);
                                timeOut = ts.Ticks / 10000;
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

        #endregion 背检

        /// <summary>
        /// 转发AOI发送的检测清除信号
        /// </summary>
        private void Remove()
        {
            while (true)
            {
                _tokenSource2?.Token.ThrowIfCancellationRequested();
                try
                {
                    long timeOut;
                    DateTime beforeDt = default;
                    byte[] value = default;
                    var clear = true;
                    var complete = true;
                    if (RemoveQueue == null)
                    {
                        continue;
                    }
                    RemoveQueue.Dequeue(out value);
                    switch (value[2])
                    {
                        case 1:
                            var destination1 = value.Skip(34).Take(10).ToArray();
                            timeOut = 0;
                            beforeDt = DateTime.Now;
                            while ((clear || complete) && timeOut < 200)
                            {
                                //发送检测清空信号
                                LockMethod(() =>
                                {
                                    var a = Aoi1Message.Find(item =>
                                        item[1] == 0 && item[2] == 3 && item.Skip(34).Take(10).ToArray()
                                            .SequenceEqual(destination1)
                                    );
                                    if (a != null)
                                    {
                                        var re = new byte[value.Length];
                                        Array.Copy(value, re, value.Length);
                                        re[1] = 0;
                                        re[2] = 3;
                                        re[3] = 1;
                                        //_remoteTcp?.Write(re);
                                        Logger?.Info(re);
                                        clear = false;
                                        Aoi1Message.RemoveAll(item => item.SequenceEqual(a));
                                    }
                                });
                                //发送就绪信号
                                LockMethod(() =>
                                {
                                    var b = Aoi1Message.Find(item =>
                                            item[1] == 1 && item[2] == 3 && item.Skip(34).Take(10).ToArray().SequenceEqual(destination1)
                                       );
                                    if (b != null)
                                    {
                                        byte[] re = new byte[value.Length];
                                        Array.Copy(value, re, value.Length);
                                        re[1] = 1;
                                        re[2] = 3;
                                        re[3] = 1;
                                        //_remoteTcp?.Write(re);
                                        Logger?.Info(re);
                                        complete = false;
                                        Aoi1Message.RemoveAll(item => item.SequenceEqual(b));
                                    }
                                });

                                var afterDt = DateTime.Now;
                                var ts = afterDt.Subtract(beforeDt);
                                timeOut = ts.Ticks / 10000;
                            }
                            break;

                        case 2:
                            var destination2 = value.Skip(84).Take(10).ToArray();
                            timeOut = 0;
                            beforeDt = DateTime.Now;
                            while ((clear || complete) && timeOut < 200)
                            {
                                //发送检测清空信号
                                LockMethod1(() =>
                                {
                                    var a = Aoi2Message.Find(item =>
                                        item[1] == 0 && item[2] == 3 && item.Skip(34).Take(10).ToArray()
                                            .SequenceEqual(destination2)
                                    );
                                    if (a != null)
                                    {
                                        var re = new byte[value.Length];
                                        Array.Copy(value, re, value.Length);
                                        re[1] = 0;
                                        re[2] = 3;
                                        re[3] = 1;
                                        //_remoteTcp?.Write(re);
                                        Logger?.Info(re);
                                        clear = false;
                                        Aoi2Message.RemoveAll(item => item.SequenceEqual(a));
                                    }
                                });

                                //发送就绪信号
                                LockMethod1(() =>
                                {
                                    var b = Aoi2Message.Find(item =>
                                    item[1] == 1 && item[2] == 3 && item.Skip(34).Take(10).ToArray().SequenceEqual(destination2)
                                );
                                    if (b != null)
                                    {
                                        byte[] re = new byte[value.Length];
                                        Array.Copy(value, re, value.Length);
                                        re[1] = 1;
                                        re[2] = 3;
                                        re[3] = 1;
                                        //_remoteTcp?.Write(re);
                                        Logger?.Info(re);
                                        complete = false;
                                        Aoi2Message.RemoveAll(item => item.SequenceEqual(b));
                                    }
                                });

                                var afterDt = DateTime.Now;
                                var ts = afterDt.Subtract(beforeDt);
                                timeOut = ts.Ticks / 10000;
                            }

                            break;

                        case 3:
                            var destination3 = value.Skip(34).Take(10).ToArray();
                            var destination4 = value.Skip(84).Take(10).ToArray();
                            timeOut = 0;
                            beforeDt = DateTime.Now;
                            while ((clear || complete) && timeOut < 200)
                            {
                                byte[] a = default;
                                byte[] a1 = default;
                                byte[] b = default;
                                byte[] b1 = default;

                                //发送检测清空信号
                                LockMethod(() =>
                                {
                                    a = Aoi1Message.Find(item =>
                                       item[1] == 0 && item[2] == 3 && item.Skip(34).Take(10).ToArray()
                                           .SequenceEqual(destination3)
                                   );
                                });
                                LockMethod1(() =>
                                {
                                    a1 = Aoi2Message.Find(item =>
                                       item[1] == 0 && item[2] == 3 && item.Skip(34).Take(10).ToArray()
                                           .SequenceEqual(destination4)
                                   );
                                });

                                if (a != null && a1 != null)
                                {
                                    var re = new byte[value.Length];
                                    Array.Copy(value, re, value.Length);
                                    re[1] = 0;
                                    re[2] = 3;
                                    re[3] = 1;
                                    //_remoteTcp?.Write(re);
                                    Logger?.Info(re);
                                    clear = false;
                                    LockMethod(() => { Aoi1Message.RemoveAll(item => item.SequenceEqual(a)); });
                                    LockMethod1(() => { Aoi2Message.RemoveAll(item => item.SequenceEqual(a1)); });
                                }

                                //发送就绪信号
                                LockMethod(() =>
                                {
                                    b = Aoi1Message.Find(item =>
                                   item[1] == 1 && item[2] == 3 && item.Skip(34).Take(44 - 34).ToArray().SequenceEqual(destination3)
                               );
                                });
                                LockMethod1(() =>
                                {
                                    b1 = Aoi2Message.Find(item =>
                                    item[1] == 1 && item[2] == 3 && item.Skip(34).Take(44 - 34).ToArray().SequenceEqual(destination4)
                                );
                                });

                                if (b != null && b1 != null)
                                {
                                    var re = new byte[value.Length];
                                    Array.Copy(value, re, value.Length);
                                    re[1] = 1;
                                    re[2] = 3;
                                    re[3] = 1;
                                    // _remoteTcp?.Write(re);
                                    Logger?.Info(re);
                                    complete = false;
                                    LockMethod(() => { Aoi1Message.RemoveAll(item => item.SequenceEqual(b)); });
                                    LockMethod1(() => { Aoi2Message.RemoveAll(item => item.SequenceEqual(b1)); });
                                }

                                var afterDt = DateTime.Now;
                                var ts = afterDt.Subtract(beforeDt);
                                timeOut = ts.Ticks / 10000;
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
                tcpButton3.Enabled = false;
                tcpRemoteBnt.Enabled = true;
                if (_remoteTcp == null) return;
                _remoteTcp.Disconnect();
                _remoteTcp.Dispose();
                _remoteTcp = null;
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
                tcpButton4.Enabled = false;
                tcpButton1.Enabled = true;
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
                if (tcpCB.Checked)
                {
                    tcpCP.Enabled = true;
                }
                else
                {
                    tcpCB.Enabled = true;
                }
                tcpButton5.Enabled = false;
                tcpButton2.Enabled = true;
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
                SpinLock.Enter(ref gotLock);
                action();
            }
            catch (Exception)
            {
            }
            finally { if (gotLock) SpinLock.Exit(); }
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
                SpinLock1.Enter(ref gotLock);
                action();
            }
            catch (Exception)
            {
            }
            finally { if (gotLock) SpinLock1.Exit(); }
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

        private string JsonPath { get; set; } = "setingTcp.Json";

        private async void SaveJsonData()
        {
            var json = new JsonObject
            {
                { "tcpCB", tcpCB.Checked },
                { "tcpCP", tcpCP.Checked },
                { "tcpNumericUpDown1", tcpNumericUpDown1.Value },
                { "tcpPlcIp", tcpPlcIp.Text },
                { "tcpPlcPort", tcpPlcPort.Text },
                { "tcpPlc_oneIp", tcpPlc_oneIp.Text },
                { "tcpPlc_onePort", tcpPlc_onePort.Text },
                { "tcpAoi1_oneIp", tcpAoi1_oneIp.Text },
                { "tcpAoi_onePort", tcpAoi_onePort.Text },
                { "tcpAoi1Ip", tcpAoi1Ip.Text },
                { "tcpAoi1Port", tcpAoi1Port.Text },
                { "tcpAoi2_oneIp", tcpAoi2_oneIp.Text },
                { "tcpAoi2_onePort", tcpAoi2_onePort.Text },
                { "tcpAoi2Ip", tcpAoi2Ip.Text },
                { "tcpAoi2Port", tcpAoi2Port.Text }
            };
            if (File.Exists(this.JsonPath))
            {
                File.Delete(JsonPath);
            }

            // Create a file to write to.
            await using var fs = File.Create(JsonPath);
            var options = new JsonSerializerOptions { WriteIndented = true };
            await JsonSerializer.SerializeAsync(fs, json, options);
            await fs.FlushAsync();
        }

        private bool InitParam()
        {
            try
            {
                if (!File.Exists(this.JsonPath)) return false;
                // 读取 配置文件
                var jsonString = File.ReadAllText(JsonPath);
                // 将 读取到的内容 反序列化 为 JSON DOM 对象
                var jsonNode = JsonNode.Parse(jsonString)!;
                // 从 DOM 对象中取值并 赋给 控件
                if (jsonNode!["tcpCB"]!.GetValue<bool>())
                {
                    tcpCB.Checked = true;
                }
                if (jsonNode!["tcpCP"]!.GetValue<bool>())
                {
                    tcpCP.Checked = true;
                }
                tcpNumericUpDown1.Value = jsonNode!["tcpNumericUpDown1"]!.GetValue<decimal>();
                tcpPlcIp.Text = jsonNode!["tcpPlcIp"]!.GetValue<string>();
                tcpPlcPort.Text = jsonNode!["tcpPlcPort"]!.GetValue<string>();
                tcpPlc_oneIp.Text = jsonNode!["tcpPlc_oneIp"]!.GetValue<string>();
                tcpPlc_onePort.Text = jsonNode!["tcpPlc_onePort"]!.GetValue<string>();

                tcpAoi1_oneIp.Text = jsonNode!["tcpAoi1_oneIp"]!.GetValue<string>();
                tcpAoi_onePort.Text = jsonNode!["tcpAoi_onePort"]!.GetValue<string>();
                tcpAoi1Ip.Text = jsonNode!["tcpAoi1Ip"]!.GetValue<string>();
                tcpAoi1Port.Text = jsonNode!["tcpAoi1Port"]!.GetValue<string>();

                tcpAoi2_oneIp.Text = jsonNode!["tcpAoi2_oneIp"]!.GetValue<string>();
                tcpAoi2_onePort.Text = jsonNode!["tcpAoi2_onePort"]!.GetValue<string>();
                tcpAoi2Ip.Text = jsonNode!["tcpAoi2Ip"]!.GetValue<string>();
                tcpAoi2Port.Text = jsonNode!["tcpAoi2Port"]!.GetValue<string>();

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private void CB_CheckedChanged(object sender, EventArgs e)
        {
            if (!tcpCB.Checked) return;
            tcpCB.Enabled = false;
            if (_tokenSource1 != null)
            {
                _tokenSource1.Cancel();
                _tokenSource1.Dispose();
                _tokenSource1 = null;
            }
            _tokenSource ??= new();
            Task.Factory.StartNew(Transmit1, _tokenSource.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            tcpCP.Enabled = true;
        }

        private void CP_CheckedChanged(object sender, EventArgs e)
        {
            if (!tcpCP.Checked) return;
            tcpCP.Enabled = false;
            if (_tokenSource != null)
            {
                _tokenSource.Cancel();
                _tokenSource.Dispose();
                _tokenSource = null;
            }
            _tokenSource1 ??= new CancellationTokenSource();
            Task.Factory.StartNew(Transmit, _tokenSource1.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            tcpCB.Enabled = true;
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
    }
}
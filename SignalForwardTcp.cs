﻿using SignalForward.UDP;
using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SignalForward
{
    public partial class SignalForwardTcp : Form
    {
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
                                byte[] buff = _localUdp1.Server.Receive(ref rEndPoint);
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
                _plcIpEndPoint = new IPEndPoint(IPAddress.Parse(tcpPlcIp.Text.Trim()), int.Parse(tcpPlcPort.Text.Trim()));

                _remoteUdp = new UdpSyncServer(IPAddress.Parse(tcpPlc_oneIp.Text.Trim()), int.Parse(tcpPlc_onePort.Text.Trim()), Logger);
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
                            Logger.Info($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}半片标识为0:");
                            Logger.Info(dataBytes);
                            Logger.Info("-------------------------");
                            break;
                    }
                };
                _remoteUdp.Start();
            }
            catch (Exception exception)
            {
                tcpRemoteBnt.Enabled = true;
                tcpButton3.Enabled = false;
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
                    var inPhoto = true;
                    //拍照完成
                    var photoCompleted = true;
                    //检测完成
                    var complete = true;
                    //收到的消息
                    byte[] value = default;
                    if (RemoteQueue == null || _plcIpEndPoint == null || RemoveQueue == null || _localUdp == null || _localUdp1 == null)
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

                            while ((inPhoto || photoCompleted || complete) && timeOut < (_timeout != default ? _timeout : 600))
                            {
                                //拍照中
                                LockMethod(() =>
                                {
                                    var a = Aoi1Message.Find(item =>
                                        item[2] == 0 && item.Skip(34).Take(44 - 34).ToArray()
                                            .SequenceEqual(destination1)
                                    );

                                    if (a != null)
                                    {
                                        var re = new byte[value.Length];
                                        Array.Copy(value, re, value.Length);
                                        re[1] = 1;
                                        re[2] = 0;
                                        re[3] = 0;
                                        _remoteUdp?.SendAsync(_plcIpEndPoint, re);
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
                                        item[2] == 1 && item.Skip(34).Take(44 - 34).ToArray()
                                            .SequenceEqual(destination1)
                                    );
                                    if (b != null)
                                    {
                                        var re = new byte[value.Length];
                                        Array.Copy(value, re, value.Length);
                                        re[1] = 1;
                                        re[2] = 1;
                                        re[3] = 0;
                                        _remoteUdp?.SendAsync(_plcIpEndPoint, re);
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
                                       item[2] == 2 && item.Skip(34).Take(44 - 34).ToArray()
                                           .SequenceEqual(destination1)
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
                            var destination2 = value.Skip(44).Take(54 - 44).ToArray();
                            timeOut = 0;
                            beforeDt = DateTime.Now;
                            while ((inPhoto || photoCompleted || complete) && timeOut < (_timeout != default ? _timeout : 600))
                            {
                                //拍照中
                                LockMethod1(() =>
                                {
                                    var a = Aoi2Message.Find(item =>
                                        item[2] == 0 && item.Skip(34).Take(44 - 34).ToArray()
                                            .SequenceEqual(destination2)
                                    );
                                    if (a != null)
                                    {
                                        var re = new byte[value.Length];
                                        Array.Copy(value, re, value.Length);
                                        re[1] = 1;
                                        re[2] = 0;
                                        re[3] = 0;
                                        _remoteUdp?.SendAsync(_plcIpEndPoint, re);
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
                                        item[2] == 1 && item.Skip(34).Take(44 - 34).ToArray()
                                            .SequenceEqual(destination2)
                                    );
                                    if (b != null)
                                    {
                                        var re = new byte[value.Length];
                                        Array.Copy(value, re, value.Length);
                                        re[1] = 1;
                                        re[2] = 1;
                                        re[3] = 0;
                                        _remoteUdp?.SendAsync(_plcIpEndPoint, re);
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
                                        item[2] == 2 && item.Skip(34).Take(44 - 34).ToArray()
                                            .SequenceEqual(destination2)
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
                            var destination3 = value.Skip(34).Take(44 - 34).ToArray();
                            var destination4 = value.Skip(44).Take(54 - 44).ToArray();
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
                                    re[2] = 0;
                                    re[3] = 0;
                                    _remoteUdp?.SendAsync(_plcIpEndPoint, re);
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
                                    re[1] = 1;
                                    re[2] = 1;
                                    re[3] = 0;
                                    _remoteUdp?.SendAsync(_plcIpEndPoint, re);
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
                                    re[1] = 1;
                                    re[2] = 2;
                                    re[3] = 0;
                                    re[9] = c[9];
                                    re[10] = c[10];
                                    re[11] = c1[9];
                                    re[12] = c1[10];
                                    _remoteUdp?.SendAsync(_plcIpEndPoint, re);
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
                    var inPhoto = true;
                    //拍照完成
                    var photoCompleted = true;
                    //检测完成
                    var complete = true;
                    //收到的消息
                    byte[] value = default;
                    if (RemoteQueue == null || _plcIpEndPoint == null || RemoveQueue == null || _localUdp == null || _localUdp1 == null)
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
                            while ((inPhoto || photoCompleted || complete) && timeOut < (_timeout != default ? _timeout : 600))
                            {
                                //拍照中
                                LockMethod(() =>
                                {
                                    var a = Aoi1Message.Find(item =>
                                        item[2] == 0 && item.Skip(34).Take(44 - 34).ToArray()
                                            .SequenceEqual(destination1)
                                    );
                                    if (a != null)
                                    {
                                        var re = new byte[value.Length];
                                        Array.Copy(value, re, value.Length);
                                        re[1] = 1;
                                        re[2] = 0;
                                        re[3] = 0;
                                        _remoteUdp?.SendAsync(_plcIpEndPoint, re);
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
                                        item[2] == 1 && item.Skip(34).Take(44 - 34).ToArray()
                                            .SequenceEqual(destination1)
                                    );
                                    if (b != null)
                                    {
                                        var re = new byte[value.Length];
                                        Array.Copy(value, re, value.Length);
                                        re[1] = 1;
                                        re[2] = 1;
                                        re[3] = 0;
                                        _remoteUdp?.SendAsync(_plcIpEndPoint, re);
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
                                        item[2] == 2 && item.Skip(34).Take(44 - 34).ToArray()
                                            .SequenceEqual(destination1)
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
                            var destination2 = value.Skip(44).Take(54 - 44).ToArray();
                            timeOut = 0;
                            beforeDt = DateTime.Now;
                            while ((inPhoto || photoCompleted || complete) && timeOut < (_timeout != default ? _timeout : 600))
                            {
                                //拍照中
                                LockMethod1(() =>
                                {
                                    var a = Aoi2Message.Find(item =>
                                        item[2] == 0 && item.Skip(34).Take(44 - 34).ToArray()
                                            .SequenceEqual(destination2)
                                    );
                                    if (a != null)
                                    {
                                        var re = new byte[value.Length];
                                        Array.Copy(value, re, value.Length);
                                        re[1] = 1;
                                        re[2] = 0;
                                        re[3] = 0;
                                        _remoteUdp?.SendAsync(_plcIpEndPoint, re);
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
                                    item[2] == 1 && item.Skip(34).Take(44 - 34).ToArray()
                                        .SequenceEqual(destination2)
                                );
                                    if (b != null)
                                    {
                                        var re = new byte[value.Length];
                                        Array.Copy(value, re, value.Length);
                                        re[1] = 1;
                                        re[2] = 1;
                                        re[3] = 0;
                                        _remoteUdp?.SendAsync(_plcIpEndPoint, re);
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
                            var destination3 = value.Skip(34).Take(44 - 34).ToArray();
                            var destination4 = value.Skip(44).Take(54 - 44).ToArray();
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
                                    a = Aoi1Message.Find(item =>
                                        item[2] == 0 && item.Skip(34).Take(44 - 34).ToArray()
                                            .SequenceEqual(destination3)
                                    );
                                });
                                LockMethod1(() =>
                                {
                                    a1 = Aoi2Message.Find(item =>
                                        item[2] == 0 && item.Skip(34).Take(44 - 34).ToArray()
                                            .SequenceEqual(destination4)
                                    );
                                });

                                if (a != null && a1 != null)
                                {
                                    var re = new byte[value.Length];
                                    Array.Copy(value, re, value.Length);
                                    re[1] = 1;
                                    re[2] = 0;
                                    re[3] = 0;
                                    _remoteUdp?.SendAsync(_plcIpEndPoint, re);
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
                                   item[2] == 1 && item.Skip(34).Take(44 - 34).ToArray().SequenceEqual(destination3)
                               );
                                });
                                LockMethod1(() =>
                                {
                                    b1 = Aoi2Message.Find(item =>
                                    item[2] == 1 && item.Skip(34).Take(44 - 34).ToArray().SequenceEqual(destination4)
                                );
                                });

                                if (b != null && b1 != null)
                                {
                                    var re = new byte[value.Length];
                                    Array.Copy(value, re, value.Length);
                                    re[1] = 1;
                                    re[2] = 1;
                                    re[3] = 0;
                                    _remoteUdp?.SendAsync(_plcIpEndPoint, re);
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
                                    item[2] == 2 && item.Skip(34).Take(44 - 34).ToArray().SequenceEqual(destination3)
                                );
                                });
                                LockMethod1(() =>
                                {
                                    c1 = Aoi2Message.Find(item =>
                                    item[2] == 2 && item.Skip(34).Take(44 - 34).ToArray().SequenceEqual(destination4)
                                );
                                });

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

        #endregion 出版

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
                            while ((clear || complete) && timeOut < 200)
                            {
                                //发送检测清空信号
                                LockMethod(() =>
                                {
                                    var a = Aoi1Message.Find(item =>
                                        item[1] == 0 && item[2] == 3 && item.Skip(34).Take(44 - 34).ToArray()
                                            .SequenceEqual(destination1)
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
                                });
                                //发送就绪信号
                                LockMethod(() =>
                                {
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
                                });

                                var afterDt = DateTime.Now;
                                var ts = afterDt.Subtract(beforeDt);
                                timeOut = ts.Ticks / 10000;
                            }
                            break;

                        case 2:
                            var destination2 = value.Skip(44).Take(54 - 44).ToArray();
                            timeOut = 0;
                            beforeDt = DateTime.Now;
                            while ((clear || complete) && timeOut < 200)
                            {
                                //发送检测清空信号
                                LockMethod1(() =>
                                {
                                    var a = Aoi2Message.Find(item =>
                                        item[1] == 0 && item[2] == 3 && item.Skip(34).Take(44 - 34).ToArray()
                                            .SequenceEqual(destination2)
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
                                        Aoi2Message.RemoveAll(item => item.SequenceEqual(a));
                                    }
                                });

                                //发送就绪信号
                                LockMethod1(() =>
                                {
                                    var b = Aoi2Message.Find(item =>
                                    item[1] == 1 && item[2] == 3 && item.Skip(34).Take(44 - 34).ToArray().SequenceEqual(destination2)
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
                                        Aoi2Message.RemoveAll(item => item.SequenceEqual(b));
                                    }
                                });

                                var afterDt = DateTime.Now;
                                var ts = afterDt.Subtract(beforeDt);
                                timeOut = ts.Ticks / 10000;
                            }

                            break;

                        case 3:
                            var destination3 = value.Skip(34).Take(44 - 34).ToArray();
                            var destination4 = value.Skip(44).Take(54 - 44).ToArray();
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
                                       item[1] == 0 && item[2] == 3 && item.Skip(34).Take(44 - 34).ToArray()
                                           .SequenceEqual(destination3)
                                   );
                                });
                                LockMethod1(() =>
                                {
                                    a1 = Aoi2Message.Find(item =>
                                       item[1] == 0 && item[2] == 3 && item.Skip(34).Take(44 - 34).ToArray()
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
                                    _remoteUdp?.SendAsync(_plcIpEndPoint, re);
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
                                    _remoteUdp?.SendAsync(_plcIpEndPoint, re);
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

        private string JsonPath { get; set; } = "seting.Json";

        private async void SaveJsonData()
        {
            var json = new JsonObject();
            json.Add("tcpCB", tcpCB.Checked);
            json.Add("tcpCP", tcpCP.Checked);
            json.Add("tcpNumericUpDown1", tcpNumericUpDown1.Value);
            json.Add("tcpPlcIp", tcpPlcIp.Text);
            json.Add("tcpPlcPort", tcpPlcPort.Text);
            json.Add("tcpPlc_oneIp", tcpPlc_oneIp.Text);
            json.Add("tcpPlc_onePort", tcpPlc_onePort.Text);

            json.Add("tcpAoi1_oneIp", tcpAoi1_oneIp.Text);
            json.Add("tcpAoi_onePort", tcpAoi_onePort.Text);
            json.Add("tcpAoi1Ip", tcpAoi1Ip.Text);
            json.Add("tcpAoi1Port", tcpAoi1Port.Text);

            json.Add("tcpAoi2_oneIp", tcpAoi2_oneIp.Text);
            json.Add("tcpAoi2_onePort", tcpAoi2_onePort.Text);
            json.Add("tcpAoi2Ip", tcpAoi2Ip.Text);
            json.Add("tcpAoi2Port", tcpAoi2Port.Text);
            if (File.Exists(this.JsonPath))
            {
                File.Delete(JsonPath);
            }

            // Create a file to write to.
            using (FileStream FS = File.Create(JsonPath))
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
                if (File.Exists(this.JsonPath))
                {
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
                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private void CB_CheckedChanged(object sender, EventArgs e)
        {
            if (tcpCB.Checked)
            {
                tcpCB.Enabled = false;
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
                tcpCP.Enabled = true;
            }
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
            if (_tokenSource1 == null)
            {
                _tokenSource1 = new();
            }
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
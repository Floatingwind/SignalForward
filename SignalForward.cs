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

        public List<byte[]> Aoi1Message = new List<byte[]>();
        public List<byte[]> Aoi2Message = new List<byte[]>();

        public SignalForward()
        {
            log = log4net.LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
            RemoteQueue = new WHCurrentQueue<byte[]>("", log);
            InitializeComponent();
            Task.Factory.StartNew(Transmit, TaskCreationOptions.LongRunning);
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
            _plcIpEndPoint = new IPEndPoint(IPAddress.Parse(PlcIp.Text.Trim()), int.Parse(PlcPort.Text.Trim()));

            _remoteUdp = new xxUDPSyncServer(IPAddress.Parse(Plc_oneIp.Text.Trim()), int.Parse(Plc_onePort.Text.Trim()), log);
            _remoteUdp.DataReceived += (object? sender, byte[] dataBytes) =>
            {
                //byte[] destination = dataBytes.Skip(34).Take(44 - 34).ToArray();

                //var b = destination.SequenceEqual(a);
                if (_localUdp != null || _localUdp1 != null)
                {
                    switch (dataBytes[43])
                    {
                        case 1:
                            _localUdp.Send(_Aoi1PortEndPoint, dataBytes);
                            break;

                        case 2:
                            _localUdp1.Send(_Aoi2PortEndPoint, dataBytes);
                            break;

                        case 3:
                            _localUdp.Send(_Aoi1PortEndPoint, dataBytes);
                            _localUdp1.Send(_Aoi2PortEndPoint, dataBytes);
                            break;

                        default:
                            break;
                    }
                    RemoteQueue.Enqueue(dataBytes);
                }
            };
            _remoteUdp.Start();
        }

        private void button1_Click(object sender, EventArgs e)
        {
            _Aoi1PortEndPoint = new IPEndPoint(IPAddress.Parse(Aoi1Ip.Text.Trim()), int.Parse(Aoi1Port.Text.Trim()));
            _localUdp = new xxUDPSyncServer(IPAddress.Parse(Aoi1_oneIp.Text.Trim()), int.Parse(Aoi_onePort.Text.Trim()), log);
            _localUdp.DataReceived += (o, bytes) =>
            {
                lock (this)
                {
                    Aoi1Message.Add(bytes);
                }
            };
        }

        private void button2_Click_1(object sender, EventArgs e)
        {
            _Aoi2PortEndPoint = new IPEndPoint(IPAddress.Parse(Aoi2Ip.Text.Trim()), int.Parse(Aoi2Port.Text.Trim()));
            _localUdp1 = new xxUDPSyncServer(IPAddress.Parse(Aoi2_oneIp.Text.Trim()), int.Parse(Aoi2_onePort.Text.Trim()), log);
            _localUdp1.DataReceived += (o, bytes) =>
            {
                lock (this)
                {
                    Aoi2Message.Add(bytes);
                }
            };
        }

        private void Transmit()
        {
            while (true)
            {
                long timeOut = 0;
                DateTime beforeDT = default;
                //拍照中
                bool inPhoto = false;
                //拍照完成
                bool photoCompleted = false;
                //检测完成
                bool complete = false;
                //收到的消息
                byte[] value = default;
                RemoteQueue.Dequeue(out value);
                log.Info(value);
                switch (value[43])
                {
                    case 1:
                        byte[] destination1 = value.Skip(34).Take(35 - 34).ToArray();
                        timeOut = 0;
                        beforeDT = System.DateTime.Now;
                        while ((inPhoto && photoCompleted && complete) || timeOut > 1000)
                        {

                            lock (this)
                            {
                                //拍照中
                                var a = Aoi1Message.Find(item =>
                                       item[2] == 0 && item.Skip(34).Take(35 - 34).ToArray().SequenceEqual(destination1)
                                   );
                                if (a != null)
                                {
                                    byte[] re = default;
                                    Array.Copy(a, re, a.Length);
                                    _remoteUdp.SendAsync(_plcIpEndPoint, re);
                                    log.Info(re);
                                    inPhoto = true;
                                    Aoi1Message.RemoveAll(item => item.SequenceEqual(a));
                                }

                                //拍照完成
                                var b = Aoi1Message.Find(item =>
                                       item[2] == 1 && item.Skip(34).Take(35 - 34).ToArray().SequenceEqual(destination1)
                                   );
                                if (b != null)
                                {
                                    byte[] re = default;
                                    Array.Copy(b, re, b.Length);
                                    _remoteUdp.SendAsync(_plcIpEndPoint, re);
                                    log.Info(re);
                                    photoCompleted = true;
                                    Aoi1Message.RemoveAll(item => item.SequenceEqual(b));
                                }

                                //检测完成
                                var c = Aoi1Message.Find(item =>
                                      item[2] == 2 && item.Skip(34).Take(35 - 34).ToArray().SequenceEqual(destination1)
                                  );
                                if (c != null)
                                {
                                    byte[] re = default;
                                    Array.Copy(c, re, c.Length);
                                    _remoteUdp.SendAsync(_plcIpEndPoint, re);
                                    log.Info(re);
                                    complete = true;
                                    Aoi1Message.RemoveAll(item => item.SequenceEqual(c));
                                }
                            }
                            DateTime afterDT = System.DateTime.Now;
                            TimeSpan ts = afterDT.Subtract(beforeDT);
                            timeOut = ts.Milliseconds;
                        }
                        break;

                    case 2:
                        byte[] destination2 = value.Skip(35).Take(36 - 35).ToArray();
                        timeOut = 0;
                        beforeDT = System.DateTime.Now;
                        while ((inPhoto && photoCompleted && complete) || timeOut > 1000)
                        {
                            lock (this)
                            {
                                //拍照中
                                var a = Aoi2Message.Find(item =>
                                    item[2] == 0 && item.Skip(35).Take(36 - 35).ToArray().SequenceEqual(destination2)
                                );
                                if (a != null)
                                {
                                    byte[] re = default;
                                    Array.Copy(a, re, a.Length);
                                    _remoteUdp.SendAsync(_plcIpEndPoint, re);
                                    log.Info(re);
                                    inPhoto = true;
                                    Aoi2Message.RemoveAll(item => item.SequenceEqual(a));
                                }

                                //拍照完成
                                var b = Aoi2Message.Find(item =>
                                    item[2] == 1 && item.Skip(35).Take(36 - 35).ToArray().SequenceEqual(destination2)
                                );
                                if (b != null)
                                {
                                    byte[] re = default;
                                    Array.Copy(b, re, b.Length);
                                    _remoteUdp.SendAsync(_plcIpEndPoint, re);
                                    log.Info(re);
                                    photoCompleted = true;
                                    Aoi2Message.RemoveAll(item => item.SequenceEqual(b));
                                }

                                //检测完成
                                var c = Aoi1Message.Find(item =>
                                    item[2] == 2 && item.Skip(35).Take(36 - 35).ToArray().SequenceEqual(destination2)
                                );
                                if (c != null)
                                {
                                    byte[] re = default;
                                    Array.Copy(c, re, c.Length);
                                    _remoteUdp.SendAsync(_plcIpEndPoint, re);
                                    log.Info(re);
                                    complete = true;
                                    Aoi2Message.RemoveAll(item => item.SequenceEqual(c));
                                }
                            }
                            DateTime afterDT = System.DateTime.Now;
                            TimeSpan ts = afterDT.Subtract(beforeDT);
                            timeOut = ts.Milliseconds;
                        }
                        break;

                    case 3:
                        byte[] destination3 = value.Skip(34).Take(35 - 34).ToArray();
                        byte[] destination4 = value.Skip(35).Take(36 - 35).ToArray();
                        timeOut = 0;
                        beforeDT = System.DateTime.Now;
                        while ((inPhoto && photoCompleted && complete) || timeOut > 1200)
                        {
                            lock (this)
                            {
                                //拍照中
                                var a = Aoi1Message.Find(item =>
                                    item[2] == 0 && item.Skip(34).Take(35 - 34).ToArray().SequenceEqual(destination3)
                                );
                                var a1 = Aoi2Message.Find(item =>
                                    item[2] == 0 && item.Skip(35).Take(36 - 35).ToArray().SequenceEqual(destination4)
                                );
                                if (a != null && a1 != null)
                                {
                                    byte[] re = default;
                                    Array.Copy(a, re, a.Length);
                                    _remoteUdp.SendAsync(_plcIpEndPoint, re);
                                    log.Info(re);
                                    inPhoto = true;
                                    Aoi1Message.RemoveAll(item => item.SequenceEqual(a));
                                    Aoi2Message.RemoveAll(item => item.SequenceEqual(a1));
                                }

                                //拍照完成
                                var b = Aoi2Message.Find(item =>
                                    item[2] == 1 && item.Skip(34).Take(35 - 34).ToArray().SequenceEqual(destination3)
                                );
                                var b1 = Aoi2Message.Find(item =>
                                    item[2] == 1 && item.Skip(35).Take(36 - 35).ToArray().SequenceEqual(destination4)
                                );
                                if (b != null && b1 != null)
                                {
                                    byte[] re = default;
                                    Array.Copy(b, re, b.Length);
                                    _remoteUdp.SendAsync(_plcIpEndPoint, re);
                                    log.Info(re);
                                    photoCompleted = true;
                                    Aoi1Message.RemoveAll(item => item.SequenceEqual(b));
                                    Aoi2Message.RemoveAll(item => item.SequenceEqual(b1));
                                }

                                //检测完成
                                var c = Aoi1Message.Find(item =>
                                    item[2] == 2 && item.Skip(34).Take(35 - 34).ToArray().SequenceEqual(destination3)
                                );
                                var c1 = Aoi1Message.Find(item =>
                                    item[2] == 2 && item.Skip(35).Take(36 - 35).ToArray().SequenceEqual(destination4)
                                );
                                if (c != null && c1 != null)
                                {
                                    c[11] = c1[9];
                                    c[12] = c1[10];
                                    byte[] re = default;
                                    Array.Copy(c, re, c.Length);
                                    _remoteUdp.SendAsync(_plcIpEndPoint, re);
                                    log.Info(re);
                                    complete = true;
                                    Aoi1Message.RemoveAll(item => item.SequenceEqual(c));
                                    Aoi2Message.RemoveAll(item => item.SequenceEqual(c1));
                                }
                            }
                            DateTime afterDT = System.DateTime.Now;
                            TimeSpan ts = afterDT.Subtract(beforeDT);
                            timeOut = ts.Milliseconds;
                        }
                        break;

                    default:
                        break;
                }
            }
        }
    }
}
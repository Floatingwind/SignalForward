using System.Net.Sockets;

namespace SignalForward.TCP
{
    public class Message
    {
        private TcpClient _tcpClient;
        private System.Text.Encoding _encoder;
        private byte _writeLineDelimiter;
        private bool _autoTrim;

        internal Message(byte[] data, TcpClient tcpClient, System.Text.Encoding stringEncoder, byte lineDelimiter)
        {
            _autoTrim = false;
            Data = data;
            _tcpClient = tcpClient ?? throw new ArgumentNullException(nameof(tcpClient));
            _encoder = stringEncoder ?? throw new ArgumentNullException(nameof(stringEncoder));
            _writeLineDelimiter = lineDelimiter;
        }

        internal Message(byte[] data, TcpClient tcpClient, System.Text.Encoding stringEncoder, byte lineDelimiter, bool autoTrim)
        {
            _autoTrim = false;
            Data = data;
            _tcpClient = tcpClient ?? throw new ArgumentNullException(nameof(tcpClient));
            _encoder = stringEncoder ?? throw new ArgumentNullException(nameof(stringEncoder));
            _writeLineDelimiter = lineDelimiter;
            _autoTrim = autoTrim;
        }

        /// <summary>
        /// 原始接收的字节数组
        /// </summary>
        public byte[] Data { get; private set; }

        /// <summary>
        /// 接收的字符串
        /// </summary>
        public string MessageString => _autoTrim ? _encoder.GetString(Data).Trim() : _encoder.GetString(Data);

        public void Reply(byte[] data)
        {
            _tcpClient.GetStream().Write(data, 0, data.Length);
        }

        public void Reply(string data)
        {
            if (string.IsNullOrEmpty(data)) { return; }
            Reply(_encoder.GetBytes(data));
        }

        public void ReplyLine(string data)
        {
            if (string.IsNullOrEmpty(data)) { return; }
            if (data.LastOrDefault() != _writeLineDelimiter)
            {
                Reply(data + _encoder.GetString(new[] { _writeLineDelimiter }));
            }
            else
            {
                Reply(data);
            }
        }

        public TcpClient TcpClient => _tcpClient;
    }
}
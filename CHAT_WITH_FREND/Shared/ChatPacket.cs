using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shared
{
    public enum PacketType { Login, Message, PrivateMessage, File, Image, UserList }

    public class ChatPacket
    {
        public PacketType Type { get; set; }
        public string Sender { get; set; }
        public string Target { get; set; }
        public string Message { get; set; }
        public byte[] FileData { get; set; }
        public string FileName { get; set; }
        public object Data { get; set; }
        public DateTime Time { get; set; }
    }
}

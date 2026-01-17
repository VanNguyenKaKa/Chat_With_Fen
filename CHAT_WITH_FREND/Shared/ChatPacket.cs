    using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shared
{
    // Thêm 3 PacketType mới
    public enum PacketType 
    { 
        Login, 
        Message, 
        PrivateMessage, 
        File, 
        Image, 
        UserList,
        FileStart,      // Bắt đầu gửi file
        FileChunk,      // Một phần của file (512KB mỗi chunk)
        FileEnd         // Kết thúc gửi file
    }

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

        // Thêm properties cho chunked transfer
        public string FileId { get; set; }      // Unique ID
        public int ChunkIndex { get; set; }     // Thứ tự chunk
        public int TotalChunks { get; set; }    // Tổng số chunks
        public long TotalFileSize { get; set; } // Tổng dung lượng
    }

    // Config
    public static class FileTransferConfig
    {
        public const int ChunkSize = 512 * 1024;           // 512KB/chunk
        public const long MaxFileSize = 2L * 1024 * 1024 * 1024; // 2GB max
    }
}

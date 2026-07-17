using System.Collections.Generic;
using System.Threading.Tasks;

namespace ZhuaQianDesktopApp
{
    public class ChatMessage
    {
        public string Role { get; set; }
        public List<ContentPart> Parts { get; set; }

        public ChatMessage()
        {
            Parts = new List<ContentPart>();
        }
    }

    public class ContentPart
    {
        public string Text { get; set; }
        public byte[] InlineData { get; set; }
        public string MimeType { get; set; }
    }

    public interface IProviderClient
    {
        string ProviderId { get; }
        bool SupportsVision { get; }
        Task<string> SendAsync(List<Dictionary<string, object>> nativeMessages, ModelInfo model,
            string apiKey, string endpoint);
        Task<string> TestConnectionAsync(ModelInfo model, string apiKey, string endpoint);
    }
}

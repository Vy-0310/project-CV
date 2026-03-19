// File: ViewModels/InterviewTurnViewModel.cs
namespace CVAnalyzer.WebApp.ViewModels
{
    // Lớp này đại diện cho một lượt hội thoại
    public class ChatMessage
    {
        public string Role { get; set; } // "system", "user", "assistant"
        public string Content { get; set; }
    }

    public class InterviewTurnViewModel
    {
        public int CvId { get; set; }
        public string UserAnswer { get; set; }
        public List<ChatMessage> History { get; set; }
    }
}
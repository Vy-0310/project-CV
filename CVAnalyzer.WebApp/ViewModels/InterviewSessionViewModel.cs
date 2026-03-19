namespace CVAnalyzer.WebApp.ViewModels
{
    public class InterviewSessionViewModel
    {
        public int CvId { get; set; }
        public string CvFileName { get; set; }
        public string FirstQuestion { get; set; }

       
        public string CandidateName { get; set; } = "Ứng viên";
        public string JobCategory { get; set; } = "General";
    }
}
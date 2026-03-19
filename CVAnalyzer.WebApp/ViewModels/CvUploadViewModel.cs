using System.ComponentModel.DataAnnotations;

namespace CVAnalyzer.WebApp.ViewModels
{
    public class CvUploadViewModel
    {
        [Required(ErrorMessage = "Vui lòng chọn ít nhất một file.")]
        [Display(Name = "Danh sách CV (PDF)")]
        
        public List<IFormFile> CvFiles { get; set; } = new List<IFormFile>();

        public string? JobDescription { get; set; }
    }
}
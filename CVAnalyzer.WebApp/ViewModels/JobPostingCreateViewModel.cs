using System.ComponentModel.DataAnnotations;

namespace CVAnalyzer.WebApp.ViewModels
{
    public class JobPostingCreateViewModel
    {
        [Required(ErrorMessage = "Vui lòng nhập Tiêu đề Vị trí.")]
        [Display(Name = "Tiêu đề Vị trí")]
        [MaxLength(255)]
        public string Title { get; set; }

        [Required(ErrorMessage = "Vui lòng dán Mô tả Công việc (JD).")]
        [Display(Name = "Mô tả Công việc (JD)")]
        public string JobDescriptionText { get; set; }
    }
}
using System.Collections.Generic;
using System.Linq;

namespace CVAnalyzer.Crawler.Services
{
    public class SlugMappingService
    {
        // Ánh xạ Tên ngành -> keyword-slug
        private readonly Dictionary<string, string> _categorySlugs = new(StringComparer.InvariantCultureIgnoreCase)
        {
            { "it", "it" },
            { "công nghệ thông tin", "it" },
            { "marketing", "marketing" },
            { "kinh doanh", "nhan-vien-kinh-doanh" },
            { "kế toán", "ke-toan" },
            { "nhân sự", "nhan-su" },
            { "thiết kế", "thiet-ke-my-thuat" }
        };

        // Ánh xạ Tên địa điểm -> location-slug
        private readonly Dictionary<string, string> _locationSlugs = new(StringComparer.InvariantCultureIgnoreCase)
        {
            { "tp.hcm", "ho-chi-minh-kl2" },
            { "hồ chí minh", "ho-chi-minh-kl2" },
            { "hà nội", "ha-noi-kl1" },
            { "đà nẵng", "da-nang-kl1" }
            // TODO: Thêm các địa điểm khác
        };

        public (string? CategorySlug, string? LocationSlug) GetSlugs(string jobCategory, string location)
        {
            // Chuẩn hóa input (viết thường, bỏ dấu, trim) để so khớp
            string categoryKey = jobCategory.ToLower().Trim();
            string locationKey = location.ToLower().Trim();

            _categorySlugs.TryGetValue(categoryKey, out var categorySlug);
            _locationSlugs.TryGetValue(locationKey, out var locationSlug);

            return (categorySlug, locationSlug);
        }
    }
}
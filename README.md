 CVAnalyzer - Hệ thống Phân tích & Đánh giá CV Tự động

Dự án này là một ứng dụng Web được xây dựng trên nền tảng .NET, kết hợp với trí tuệ nhân tạo (AI) để hỗ trợ việc phân tích, trích xuất dữ liệu và đánh giá nội dung CV một cách tự động và thông minh.

Các chức năng chính (Core Features)

* Phân tích CV thông minh: Sử dụng OpenAI API để đọc hiểu nội dung CV, đánh giá độ phù hợp của ứng viên với mô tả công việc.
* Hệ thống Crawler (CVAnalyzer.Crawler): Tự động thu thập và xử lý dữ liệu từ các nguồn khác nhau, tối ưu hóa việc quản lý hồ sơ.
* Quản lý dữ liệu tập trung (CVAnalyzer.Data): Sử dụng Entity Framework Core để quản lý cơ sở dữ liệu ứng viên và kết quả đánh giá một cách hệ thống.
* Giao diện trực quan (CVAnalyzer.WebApp): Cung cấp bộ công cụ đăng nhập, quản lý và theo dõi kết quả phân tích dành cho người dùng.

 Công nghệ sử dụng (Tech Stack)

* Backend: ASP.NET Core MVC / Web API, C#
* AI Integration: OpenAI Service (GPT Model)
* Database & ORM: MySql, Entity Framework Core
* Frontend: HTML5, CSS3, JavaScript, Razor Pages
* Kiến trúc: Clean Architecture (tách biệt giữa WebApp, Crawler và Data layer)

 Hướng dẫn cài đặt (Getting Started)

1. Clone mã nguồn:   
    git clone [https://github.com/Luan31-10/CVAnalyzer.git](https://github.com/Luan31-10/CVAnalyzer.git)
2.  Cấu hình Database:
     Mở dự án trong Visual Studio 2022.
     Cập nhật chuỗi kết nối 'DefaultConnection' trong file 'appsettings.json' cho phù hợp với MySql cục bộ.
3.  Cấu hình AI:
     Thay thế 'YOUR_OPENAI_API_KEY_HERE' bằng Key cá nhân của bạn trong file 'appsettings.json'.
4.  Chạy ứng dụng:
     Nhấn 'F5' để khởi chạy dự án WebApp.

 Thông tin tác giả
* Võ Thành Luân - Sinh viên chuyên ngành Công nghệ Phần mềm - ĐH HUTECH
* GitHub: [https://github.com/Luan31-10](https://github.com/Luan31-10)

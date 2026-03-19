// Logic cho chức năng đổi giao diện Sáng/Tối
(function () {
    'use strict';

    const themeToggleBtn = document.getElementById('theme-toggle');
    if (!themeToggleBtn) {
        return;
    }

    const htmlElement = document.documentElement;
    const currentTheme = localStorage.getItem('theme') || 'dark';

    // Áp dụng theme đã lưu khi tải trang
    htmlElement.setAttribute('data-bs-theme', currentTheme);
    updateButtonIcon(currentTheme);

    // Xử lý khi nhấn nút
    themeToggleBtn.addEventListener('click', function (e) {
        e.preventDefault();

        // Lấy theme hiện tại và chuyển đổi
        let theme = htmlElement.getAttribute('data-bs-theme') === 'dark' ? 'light' : 'dark';

        // Lưu lựa chọn mới vào localStorage
        localStorage.setItem('theme', theme);

        // Áp dụng theme mới vào trang
        htmlElement.setAttribute('data-bs-theme', theme);

        // Cập nhật lại icon trên nút
        updateButtonIcon(theme);
    });

    function updateButtonIcon(theme) {
        const icon = themeToggleBtn.querySelector('i');
        if (icon) {
            if (theme === 'dark') {
                icon.classList.remove('bi-sun-fill');
                icon.classList.add('bi-moon-stars-fill');
            } else {
                icon.classList.remove('bi-moon-stars-fill');
                icon.classList.add('bi-sun-fill');
            }
        }
    }
})();
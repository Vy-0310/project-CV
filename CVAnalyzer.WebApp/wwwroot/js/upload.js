document.addEventListener("DOMContentLoaded", function () {
    // --- Logic cho hiệu ứng Loading trên nút Submit ---
    const uploadForm = document.getElementById("upload-form");
    const submitButton = document.getElementById("submit-button");
    const buttonText = submitButton.querySelector(".button-text");
    const spinner = submitButton.querySelector(".spinner-border");

    if (uploadForm) {
        uploadForm.addEventListener("submit", function () {
            // Chỉ chạy khi form hợp lệ (đã chọn file)
            if (uploadForm.checkValidity()) {
                submitButton.disabled = true;
                buttonText.textContent = "Đang phân tích...";
                spinner.classList.remove("d-none");
            }
        });
    }

    // --- Logic cho khu vực Drag & Drop ---
    document.querySelectorAll(".drop-zone__input").forEach((inputElement) => {
        const dropZoneElement = inputElement.closest(".drop-zone");

        dropZoneElement.addEventListener("click", (e) => {
            // Chỉ kích hoạt input nếu bấm vào vùng trống, không phải bấm vào thumbnail
            if (!dropZoneElement.querySelector(".file-thumbnail")) {
                inputElement.click();
            }
        });

        inputElement.addEventListener("change", (e) => {
            if (inputElement.files.length) {
                updateThumbnail(dropZoneElement, inputElement.files[0]);
            }
        });

        dropZoneElement.addEventListener("dragover", (e) => {
            e.preventDefault();
            dropZoneElement.classList.add("drop-zone--over");
        });

        ["dragleave", "dragend"].forEach((type) => {
            dropZoneElement.addEventListener(type, (e) => {
                dropZoneElement.classList.remove("drop-zone--over");
            });
        });

        dropZoneElement.addEventListener("drop", (e) => {
            e.preventDefault();
            if (e.dataTransfer.files.length) {
                inputElement.files = e.dataTransfer.files;
                updateThumbnail(dropZoneElement, e.dataTransfer.files[0]);
            }
            dropZoneElement.classList.remove("drop-zone--over");
        });
    });

    /**
     * Cập nhật thumbnail (hiển thị file đã chọn)
     * @param {HTMLElement} dropZoneElement
     * @param {File} file
     */
    function updateThumbnail(dropZoneElement, file) {
        let thumbnailElement = dropZoneElement.querySelector(".file-thumbnail");
        const promptElement = dropZoneElement.querySelector(".drop-zone__prompt");

        // 1. Ẩn dòng chữ "Kéo thả..."
        if (promptElement) {
            promptElement.style.display = "none";
        }

        // 2. Nếu thumbnail chưa tồn tại, tạo nó
        if (!thumbnailElement) {
            thumbnailElement = document.createElement("div");
            thumbnailElement.classList.add("file-thumbnail");
            dropZoneElement.appendChild(thumbnailElement);
        }

        // 3. Tính toán dung lượng file
        let fileSize = (file.size / 1024 / 1024).toFixed(2); // MB

        // 4. Tạo cấu trúc HTML cho thumbnail
        // (Cần có Bootstrap Icons trên trang của bạn)
        thumbnailElement.innerHTML = `
            <div class="file-thumbnail-icon">
                <i class="bi bi-file-earmark-pdf-fill"></i>
            </div>
            <div class="file-thumbnail-info">
                <span class="file-name">${file.name}</span>
                <span class="file-size">${fileSize} MB</span>
            </div>
        `;

        // Thay đổi style của drop-zone để không còn là "dashed" nữa
        dropZoneElement.style.borderStyle = "solid";
        dropZoneElement.style.backgroundColor = "var(--bs-tertiary-bg)";
    }
});
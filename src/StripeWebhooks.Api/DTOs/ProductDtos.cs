using System.ComponentModel.DataAnnotations;

namespace StripeWebhooks.Api.DTOs;

public record CreateProductDto(
    int? Id, // Tùy chọn: Nhập Id thủ công hoặc để trống để DB tự tăng

    [property: Required(ErrorMessage = "Tên sản phẩm không được để trống")]
    [property: StringLength(100, ErrorMessage = "Tên không quá 100 ký tự")]
    string Name,

    [property: Range(0.01, double.MaxValue, ErrorMessage = "Giá sản phẩm phải lớn hơn 0")]
    decimal Price,

    [property: Range(0, int.MaxValue, ErrorMessage = "Số lượng tồn kho không được âm")]
    int StockQuantity
);

public record UpdateProductDto(
    [property: Required(ErrorMessage = "Tên sản phẩm không được để trống")]
    [property: StringLength(100, ErrorMessage = "Tên không quá 100 ký tự")]
    string Name,

    [property: Range(0.01, double.MaxValue, ErrorMessage = "Giá sản phẩm phải lớn hơn 0")]
    decimal Price,

    [property: Range(0, int.MaxValue, ErrorMessage = "Số lượng tồn kho không được âm")]
    int StockQuantity
);

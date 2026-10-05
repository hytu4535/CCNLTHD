using System.ComponentModel.DataAnnotations;

namespace StripeWebhooks.Api.DTOs;

public sealed record CreateProductDto(
    [property: Range(1, int.MaxValue, ErrorMessage = "Id phải lớn hơn 0")]
    int? Id,

    [property: Required(ErrorMessage = "Tên sản phẩm không được để trống")]
    [property: StringLength(100, ErrorMessage = "Tên không quá 100 ký tự")]
    string Name,

    [property: Range(typeof(decimal), "0.01", "79228162514264337593543950335", ErrorMessage = "Giá sản phẩm phải lớn hơn 0")]
    decimal Price,

    [property: Range(0, int.MaxValue, ErrorMessage = "Số lượng tồn kho không được âm")]
    int StockQuantity
);

public sealed record UpdateProductDto(
    [property: Required(ErrorMessage = "Tên sản phẩm không được để trống")]
    [property: StringLength(100, ErrorMessage = "Tên không quá 100 ký tự")]
    string Name,

    [property: Range(typeof(decimal), "0.01", "79228162514264337593543950335", ErrorMessage = "Giá sản phẩm phải lớn hơn 0")]
    decimal Price,

    [property: Range(0, int.MaxValue, ErrorMessage = "Số lượng tồn kho không được âm")]
    int StockQuantity
);

public sealed record ProductResponse(
    int Id,
    string Name,
    decimal Price,
    int StockQuantity,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt
);

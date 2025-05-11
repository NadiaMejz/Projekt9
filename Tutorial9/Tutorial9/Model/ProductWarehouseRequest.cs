namespace Tutorial9.Model;
// DTO przyjmowany w Body
public sealed record ProductWarehouseRequest
(
    int IdProduct,
    int IdWarehouse,
    int Amount,
    DateTime CreatedAt
);

// DTO zwracany po szczęśliwym zapisie
public sealed record ProductWarehouseResponse(int IdProductWarehouse);
